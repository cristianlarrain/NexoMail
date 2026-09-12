using Microsoft.EntityFrameworkCore;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure;

public enum AiUsagePeriod { Week, Month }

public sealed record AiUsageSummaryDto(
    string Period,
    DateTimeOffset CurrentStart,
    DateTimeOffset CurrentEnd,
    decimal CurrentCostClp,
    decimal PreviousCostClp,
    decimal? VariationPercent,
    long Operations,
    int ActiveUsers,
    decimal AverageCostPerActiveUserClp);

public sealed record AiUsageSettingsDto(decimal GreenMaxClp, decimal YellowMaxClp, decimal ReferenceClpPerUsd);

public sealed record AiUsageUserRowDto(
    Guid UserId,
    string DisplayName,
    string Email,
    string PlanCode,
    string EffectivePlanCode,
    bool IsTrialActive,
    string? TrialType,
    DateTimeOffset? TrialStart,
    DateTimeOffset? TrialEndsAt,
    int? TrialDaysRemaining,
    long WeekOperations,
    long InputTokens,
    long OutputTokens,
    decimal WeekCostClp,
    decimal AccumulatedCostClp,
    decimal AverageDailyCostClp,
    decimal ProjectedCostClp,
    bool IsInitialProjection,
    decimal? SevenDayProjectedCostClp,
    decimal? SevenDayTrendPercent,
    string CostStatus);

public sealed record AiUsageDailyDto(DateOnly Date, long Operations, long InputTokens, long OutputTokens, decimal CostClp);
public sealed record AiUsageOperationDto(string OperationType, long Operations, decimal CostClp);
public sealed record AiUsageUserDetailDto(AiUsageUserRowDto User, IReadOnlyList<AiUsageDailyDto> Daily, IReadOnlyList<AiUsageOperationDto> Operations);

public sealed class AiUsageAdminService(NexoMailDbContext database, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<AiUsageSummaryDto> GetSummaryAsync(AiUsagePeriod period, CancellationToken ct)
    {
        await AiUsageSchemaBootstrap.EnsureAsync(database, ct);
        var now = _timeProvider.GetUtcNow();
        var (currentStart, currentEnd, previousStart, previousEnd) = PeriodBounds(period, now);
        var events = await database.AiUsageEvents.AsNoTracking()
            .Where(x => x.OccurredAt >= previousStart.UtcDateTime && x.OccurredAt < currentEnd.UtcDateTime)
            .Select(x => new { x.UserId, x.OccurredAt, x.EstimatedCostClp })
            .ToArrayAsync(ct);

        var current = events.Where(x => x.OccurredAt >= currentStart.UtcDateTime && x.OccurredAt < currentEnd.UtcDateTime).ToArray();
        var previous = events.Where(x => x.OccurredAt >= previousStart.UtcDateTime && x.OccurredAt < previousEnd.UtcDateTime).ToArray();
        var currentCost = current.Sum(x => x.EstimatedCostClp ?? 0m);
        var previousCost = previous.Sum(x => x.EstimatedCostClp ?? 0m);
        var activeUsers = current.Select(x => x.UserId).Distinct().Count();
        decimal? variation = previousCost == 0m
            ? null
            : decimal.Round((currentCost - previousCost) / previousCost * 100m, 2);

        return new AiUsageSummaryDto(
            period == AiUsagePeriod.Week ? "week" : "month",
            currentStart,
            currentEnd,
            currentCost,
            previousCost,
            variation,
            current.LongLength,
            activeUsers,
            activeUsers == 0 ? 0m : decimal.Round(currentCost / activeUsers, 2));
    }

    public async Task<IReadOnlyList<AiUsageUserRowDto>> GetUsersAsync(string? sort, CancellationToken ct)
    {
        await AiUsageSchemaBootstrap.EnsureAsync(database, ct);
        var settings = await GetSettingsEntityAsync(ct);
        var users = await database.Users.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.Email)
            .ToArrayAsync(ct);
        var now = _timeProvider.GetUtcNow();
        var from = now.AddMonths(-12).UtcDateTime;
        var events = await database.AiUsageEvents.AsNoTracking()
            .Where(x => x.OccurredAt >= from)
            .ToArrayAsync(ct);

        var rows = new List<AiUsageUserRowDto>(users.Length);
        foreach (var user in users)
        {
            var userEvents = events.Where(x => x.UserId == user.Id).ToArray();
            rows.Add(await BuildUserRowAsync(user, userEvents, settings, now, ct));
        }

        return (sort ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "accumulated" => rows.OrderByDescending(x => x.AccumulatedCostClp).ThenBy(x => x.DisplayName).ToArray(),
            _ => rows.OrderByDescending(x => x.ProjectedCostClp).ThenBy(x => x.DisplayName).ToArray()
        };
    }

    public async Task<AiUsageUserDetailDto?> GetUserAsync(Guid userId, CancellationToken ct)
    {
        await AiUsageSchemaBootstrap.EnsureAsync(database, ct);
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
        if (user is null) return null;

        var settings = await GetSettingsEntityAsync(ct);
        var now = _timeProvider.GetUtcNow();
        var lastYear = now.AddMonths(-12).UtcDateTime;
        var allEvents = await database.AiUsageEvents.AsNoTracking()
            .Where(x => x.UserId == userId && x.OccurredAt >= lastYear)
            .OrderBy(x => x.OccurredAt)
            .ToArrayAsync(ct);
        var row = await BuildUserRowAsync(user, allEvents, settings, now, ct);

        var detailStart = now.AddDays(-30).UtcDateTime;
        var detailEvents = allEvents.Where(x => x.OccurredAt >= detailStart && x.OccurredAt <= now.UtcDateTime).ToArray();
        var daily = detailEvents
            .GroupBy(x => DateOnly.FromDateTime(x.OccurredAt))
            .OrderBy(x => x.Key)
            .Select(group => new AiUsageDailyDto(
                group.Key,
                group.LongCount(),
                group.Sum(x => x.InputTokens),
                group.Sum(x => x.OutputTokens),
                group.Sum(x => x.EstimatedCostClp ?? 0m)))
            .ToArray();
        var operations = detailEvents
            .GroupBy(x => x.OperationType, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new AiUsageOperationDto(
                group.Key,
                group.LongCount(),
                group.Sum(x => x.EstimatedCostClp ?? 0m)))
            .ToArray();

        return new AiUsageUserDetailDto(row, daily, operations);
    }

    public async Task<AiUsageSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        await AiUsageSchemaBootstrap.EnsureAsync(database, ct);
        var settings = await GetSettingsEntityAsync(ct);
        return new AiUsageSettingsDto(settings.GreenMaxClp, settings.YellowMaxClp, settings.ReferenceClpPerUsd);
    }

    public async Task<AiUsageSettingsDto> UpdateSettingsAsync(
        decimal greenMaxClp,
        decimal yellowMaxClp,
        decimal referenceClpPerUsd,
        CancellationToken ct)
    {
        if (greenMaxClp < 0m || yellowMaxClp <= greenMaxClp)
            throw new InvalidOperationException("Los umbrales de consumo no son válidos.");
        if (referenceClpPerUsd <= 0m)
            throw new InvalidOperationException("El tipo de cambio de referencia debe ser mayor que cero.");

        await AiUsageSchemaBootstrap.EnsureAsync(database, ct);
        var settings = await GetSettingsEntityAsync(ct);
        settings.GreenMaxClp = greenMaxClp;
        settings.YellowMaxClp = yellowMaxClp;
        settings.ReferenceClpPerUsd = referenceClpPerUsd;
        settings.UpdatedAt = _timeProvider.GetUtcNow();
        await database.SaveChangesAsync(ct);
        return new AiUsageSettingsDto(settings.GreenMaxClp, settings.YellowMaxClp, settings.ReferenceClpPerUsd);
    }

    private async Task<AiUsageUserRowDto> BuildUserRowAsync(
        UserEntity user,
        IReadOnlyCollection<AiUsageEventEntity> events,
        AiUsageSettingsEntity settings,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var access = await CommercialAccessStore.GetAsync(database, user.Id, ct);
        var subscription = access?.Subscription;
        var isTrial = subscription is not null
            && string.Equals(subscription.Provider, CommercialAccessStore.AdminTrialProvider, StringComparison.OrdinalIgnoreCase)
            && string.Equals(subscription.Status, CommercialSubscriptionStatuses.Trialing, StringComparison.OrdinalIgnoreCase)
            && subscription.CurrentPeriodStart is not null
            && subscription.TrialEndsAt is not null
            && subscription.TrialEndsAt > now;

        var weekStart = StartOfWeek(now);
        var weekEvents = events.Where(x => x.OccurredAt >= weekStart.UtcDateTime && x.OccurredAt <= now.UtcDateTime).ToArray();
        var weekCost = weekEvents.Sum(x => x.EstimatedCostClp ?? 0m);
        var weekOperations = weekEvents.LongLength;
        var inputTokens = events.Sum(x => x.InputTokens);
        var outputTokens = events.Sum(x => x.OutputTokens);

        DateTimeOffset? trialStart = isTrial ? subscription!.CurrentPeriodStart : null;
        DateTimeOffset? trialEnd = isTrial ? subscription!.TrialEndsAt : null;
        var trialEvents = isTrial
            ? events.Where(x => x.OccurredAt >= trialStart!.Value.UtcDateTime && x.OccurredAt <= now.UtcDateTime).ToArray()
            : Array.Empty<AiUsageEventEntity>();

        decimal accumulated;
        decimal averageDaily;
        decimal projected;
        bool initial;
        decimal? sevenDayProjection = null;
        decimal? sevenDayTrend = null;
        int? remaining = null;

        if (isTrial)
        {
            accumulated = trialEvents.Sum(x => x.EstimatedCostClp ?? 0m);
            var totalDays = Math.Max(1d, (trialEnd!.Value - trialStart!.Value).TotalDays);
            var actualElapsed = Math.Max(0d, (now - trialStart.Value).TotalDays);
            var elapsedDays = Math.Max(1d, actualElapsed);
            initial = actualElapsed < 1d;
            averageDaily = decimal.Round(accumulated / (decimal)elapsedDays, 2);
            projected = decimal.Round(averageDaily * (decimal)totalDays, 2);
            remaining = Math.Max(0, (int)Math.Ceiling((trialEnd.Value - now).TotalDays));

            if (actualElapsed >= 7d)
            {
                var lastSevenStart = now.AddDays(-7).UtcDateTime;
                var lastSevenCost = trialEvents.Where(x => x.OccurredAt >= lastSevenStart).Sum(x => x.EstimatedCostClp ?? 0m);
                var recentAverage = lastSevenCost / 7m;
                sevenDayProjection = decimal.Round(recentAverage * (decimal)totalDays, 2);
                sevenDayTrend = averageDaily == 0m
                    ? null
                    : decimal.Round((recentAverage - averageDaily) / averageDaily * 100m, 2);
            }
        }
        else
        {
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEvents = events.Where(x => x.OccurredAt >= monthStart && x.OccurredAt <= now.UtcDateTime).ToArray();
            accumulated = monthEvents.Sum(x => x.EstimatedCostClp ?? 0m);
            var lastSevenStart = now.AddDays(-7).UtcDateTime;
            var recentCost = events.Where(x => x.OccurredAt >= lastSevenStart && x.OccurredAt <= now.UtcDateTime).Sum(x => x.EstimatedCostClp ?? 0m);
            averageDaily = decimal.Round(recentCost / 7m, 2);
            projected = decimal.Round(averageDaily * 30m, 2);
            initial = false;
        }

        var status = projected <= settings.GreenMaxClp
            ? "verde"
            : projected <= settings.YellowMaxClp ? "amarillo" : "rojo";
        var trialType = isTrial
            ? string.Equals(access?.EffectivePlan.Code, CommercialPlanCatalog.Premium, StringComparison.OrdinalIgnoreCase) ? "premium" : "nexi"
            : null;

        return new AiUsageUserRowDto(
            user.Id,
            user.DisplayName,
            user.Email,
            user.PlanCode,
            access?.EffectivePlan.Code ?? user.PlanCode,
            isTrial,
            trialType,
            trialStart,
            trialEnd,
            remaining,
            weekOperations,
            inputTokens,
            outputTokens,
            weekCost,
            accumulated,
            averageDaily,
            projected,
            initial,
            sevenDayProjection,
            sevenDayTrend,
            status);
    }

    private async Task<AiUsageSettingsEntity> GetSettingsEntityAsync(CancellationToken ct)
    {
        var settings = await database.AiUsageSettings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (settings is not null) return settings;

        settings = new AiUsageSettingsEntity
        {
            Id = 1,
            GreenMaxClp = 1500m,
            YellowMaxClp = 3000m,
            ReferenceClpPerUsd = 941.1m,
            UpdatedAt = _timeProvider.GetUtcNow()
        };
        database.AiUsageSettings.Add(settings);
        await database.SaveChangesAsync(ct);
        return settings;
    }

    private static (DateTimeOffset CurrentStart, DateTimeOffset CurrentEnd, DateTimeOffset PreviousStart, DateTimeOffset PreviousEnd)
        PeriodBounds(AiUsagePeriod period, DateTimeOffset now)
    {
        if (period == AiUsagePeriod.Week)
        {
            var currentStart = StartOfWeek(now);
            return (currentStart, now.AddTicks(1), currentStart.AddDays(-7), currentStart);
        }

        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var previousStart = monthStart.AddMonths(-1);
        return (monthStart, now.AddTicks(1), previousStart, monthStart);
    }

    private static DateTimeOffset StartOfWeek(DateTimeOffset now)
    {
        var date = now.UtcDateTime.Date;
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return new DateTimeOffset(date.AddDays(-daysSinceMonday), TimeSpan.Zero);
    }
}
