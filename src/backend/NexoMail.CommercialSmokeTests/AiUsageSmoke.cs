using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;

internal static class AiUsageSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync(CancellationToken.None).GetAwaiter().GetResult();

    public static async Task RunAsync(CancellationToken ct)
    {
        await VerifyCostAndSchemaAsync(ct);
        await VerifyTrackerAsync(ct);
    }

    private static async Task VerifyCostAndSchemaAsync(CancellationToken ct)
    {
        var pricing = new AiUsagePriceCatalogOptions
        {
            Models = new Dictionary<string, AiUsageModelPrice>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt-5.6-luna"] = new(0.20m, 1.20m)
            }
        };
        var calculator = new AiUsageCostCalculator(Options.Create(pricing));

        var cost = calculator.Calculate("gpt-5.6-luna", 1_000_000, 1_000_000, 941.1m);
        Require(cost is not null, "Known model must have calculable cost.");
        Require(cost!.EstimatedCostUsd == 1.40m, "Known model USD cost is wrong.");
        Require(cost.EstimatedCostClp == decimal.Round(1.40m * 941.1m, 4), "CLP conversion is wrong.");
        Require(calculator.Calculate("unknown-model", 1000, 1000, 941.1m) is null,
            "Unknown model must not invent cost.");

        var dbPath = TempDbPath();
        try
        {
            await using var database = CreateDatabase(dbPath);
            await database.Database.EnsureCreatedAsync(ct);

            var user = NewUser();
            database.Users.Add(user);
            database.AiUsageEvents.Add(new AiUsageEventEntity
            {
                Id = Guid.NewGuid(), UserId = user.Id, OccurredAt = DateTimeOffset.UtcNow.UtcDateTime,
                OperationType = "mail_summary", Model = "gpt-5.6-luna", InputTokens = 10,
                OutputTokens = 5, DurationMs = 25, Succeeded = true
            });
            database.AiUsageMonthlySummaries.Add(new AiUsageMonthlySummaryEntity
            {
                UserId = user.Id, Year = 2026, Month = 9, OperationCount = 1,
                SuccessfulOperations = 1, InputTokens = 10, OutputTokens = 5,
                ActiveDays = 1, UpdatedAt = DateTimeOffset.UtcNow
            });
            database.AiUsageSettings.Add(new AiUsageSettingsEntity
            {
                Id = 1, GreenMaxClp = 1500m, YellowMaxClp = 3000m,
                ReferenceClpPerUsd = 941.1m, UpdatedAt = DateTimeOffset.UtcNow
            });
            await database.SaveChangesAsync(ct);

            Require(await database.AiUsageEvents.CountAsync(ct) == 1, "Usage event schema must be writable.");
            Require(await database.AiUsageMonthlySummaries.CountAsync(ct) == 1, "Monthly summary schema must be writable.");
            Require(await database.AiUsageSettings.SingleAsync(ct) is not null, "Usage settings schema must be writable.");
        }
        finally { DeleteDb(dbPath); }
    }

    private static async Task VerifyTrackerAsync(CancellationToken ct)
    {
        var dbPath = TempDbPath();
        try
        {
            await using var database = CreateDatabase(dbPath);
            await database.Database.EnsureCreatedAsync(ct);
            var user = NewUser();
            database.Users.Add(user);
            await database.SaveChangesAsync(ct);

            var pricing = new AiUsagePriceCatalogOptions
            {
                DefaultReferenceClpPerUsd = 941.1m,
                Models = new Dictionary<string, AiUsageModelPrice>(StringComparer.OrdinalIgnoreCase)
                {
                    ["gpt-5.6-luna"] = new(0.20m, 1.20m)
                }
            };
            var calculator = new AiUsageCostCalculator(Options.Create(pricing));
            var tracker = new AiUsageTracker(database, calculator, Options.Create(pricing));

            await tracker.RecordAsync(new AiUsageRecord(
                user.Id, "mail_summary", "gpt-5.6-luna", 1000, 200, null, 120, true, null,
                new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero)), ct);
            await tracker.RecordAsync(new AiUsageRecord(
                user.Id, "mail_summary", "gpt-5.6-luna", 500, 100, null, 90, true, null,
                new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero)), ct);
            await tracker.RecordAsync(new AiUsageRecord(
                user.Id, "mail_context_analysis", "gpt-5.6-luna", 750, 150, null, 150, true, null,
                new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero)), ct);

            Require(await database.AiUsageEvents.CountAsync(ct) == 3, "Each provider call must create one usage event.");
            var monthly = await database.AiUsageMonthlySummaries.SingleAsync(ct);
            Require(monthly.OperationCount == 3, "Same-month usage must accumulate in one monthly row.");
            Require(monthly.SuccessfulOperations == 3 && monthly.FailedOperations == 0, "Monthly success counts are wrong.");
            Require(monthly.InputTokens == 2250 && monthly.OutputTokens == 450, "Monthly token totals are wrong.");
            Require(monthly.ActiveDays == 2, "Monthly active days must count distinct calendar days.");
            var knownCost = monthly.EstimatedCostUsd;

            await tracker.RecordAsync(new AiUsageRecord(
                user.Id, "other", "unknown-model", 900, 100, null, 40, true, null,
                new DateTimeOffset(2026, 9, 12, 11, 0, 0, TimeSpan.Zero)), ct);

            var unknownEvent = await database.AiUsageEvents.OrderByDescending(x => x.OccurredAt).FirstAsync(ct);
            Require(unknownEvent.EstimatedCostUsd is null && unknownEvent.EstimatedCostClp is null,
                "Unknown model must keep cost non-calculable.");
            await database.Entry(monthly).ReloadAsync(ct);
            Require(monthly.EstimatedCostUsd == knownCost, "Unknown-model usage must not change calculable monthly cost.");
        }
        finally { DeleteDb(dbPath); }
    }

    private static NexoMailDbContext CreateDatabase(string path) => new(
        new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);

    private static UserEntity NewUser() => new()
    {
        Id = Guid.NewGuid(), DisplayName = "AI Usage Smoke",
        Email = $"ai-usage-{Guid.NewGuid():N}@nexomail.test", CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true, IsEmailVerified = true
    };

    private static string TempDbPath() => Path.Combine(Path.GetTempPath(), $"nexomail-ai-usage-smoke-{Guid.NewGuid():N}.db");

    private static void DeleteDb(string dbPath)
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = dbPath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
