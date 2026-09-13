using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure;

public sealed record AiUsageRecord(
    Guid UserId,
    string OperationType,
    string Model,
    long InputTokens,
    long OutputTokens,
    long? ReasoningTokens,
    long DurationMs,
    bool Succeeded,
    string? ErrorCategory,
    DateTimeOffset OccurredAt);

public interface IAiUsageTracker
{
    Task RecordAsync(AiUsageRecord record, CancellationToken ct);
}

public sealed class AiUsageTracker(
    NexoMailDbContext database,
    AiUsageCostCalculator costCalculator,
    IOptions<AiUsagePriceCatalogOptions> pricingOptions) : IAiUsageTracker
{
    public async Task RecordAsync(AiUsageRecord record, CancellationToken ct)
    {
        await AiUsageSchemaBootstrap.EnsureAsync(database, ct);
        await using var transaction = await database.Database.BeginTransactionAsync(ct);

        var settings = await database.AiUsageSettings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (settings is null)
        {
            settings = new AiUsageSettingsEntity
            {
                Id = 1,
                GreenMaxClp = 1500m,
                YellowMaxClp = 3000m,
                ReferenceClpPerUsd = pricingOptions.Value.DefaultReferenceClpPerUsd,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            database.AiUsageSettings.Add(settings);
        }

        var cost = costCalculator.Calculate(
            record.Model,
            record.InputTokens,
            record.OutputTokens,
            settings.ReferenceClpPerUsd);
        var occurredUtc = record.OccurredAt.UtcDateTime;

        database.AiUsageEvents.Add(new AiUsageEventEntity
        {
            Id = Guid.NewGuid(),
            UserId = record.UserId,
            OccurredAt = occurredUtc,
            OperationType = record.OperationType,
            Model = record.Model,
            InputTokens = record.InputTokens,
            OutputTokens = record.OutputTokens,
            ReasoningTokens = record.ReasoningTokens,
            DurationMs = record.DurationMs,
            Succeeded = record.Succeeded,
            ErrorCategory = record.ErrorCategory,
            InputUsdPerMillion = cost?.InputUsdPerMillion,
            OutputUsdPerMillion = cost?.OutputUsdPerMillion,
            EstimatedCostUsd = cost?.EstimatedCostUsd,
            ClpPerUsd = cost?.ClpPerUsd,
            EstimatedCostClp = cost?.EstimatedCostClp
        });

        var year = occurredUtc.Year;
        var month = occurredUtc.Month;
        var summary = await database.AiUsageMonthlySummaries.SingleOrDefaultAsync(
            x => x.UserId == record.UserId && x.Year == year && x.Month == month,
            ct);

        if (summary is null)
        {
            summary = new AiUsageMonthlySummaryEntity
            {
                UserId = record.UserId,
                Year = year,
                Month = month,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            database.AiUsageMonthlySummaries.Add(summary);
        }

        var dayStart = new DateTime(occurredUtc.Year, occurredUtc.Month, occurredUtc.Day, 0, 0, 0, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);
        var hadActivityThatDay = await database.AiUsageEvents
            .AsNoTracking()
            .AnyAsync(x => x.UserId == record.UserId && x.OccurredAt >= dayStart && x.OccurredAt < dayEnd, ct);

        summary.OperationCount++;
        if (record.Succeeded) summary.SuccessfulOperations++;
        else summary.FailedOperations++;
        summary.InputTokens += record.InputTokens;
        summary.OutputTokens += record.OutputTokens;
        if (cost is not null)
        {
            summary.EstimatedCostUsd += cost.EstimatedCostUsd;
            summary.EstimatedCostClp += cost.EstimatedCostClp;
        }
        if (!hadActivityThatDay) summary.ActiveDays++;
        summary.UpdatedAt = DateTimeOffset.UtcNow;

        await database.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
