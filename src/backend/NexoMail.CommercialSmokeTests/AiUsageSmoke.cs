using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;

internal static class AiUsageSmoke
{
    public static async Task RunAsync(CancellationToken ct)
    {
        var calculator = new AiUsageCostCalculator(Options.Create(new AiUsagePriceCatalogOptions
        {
            Models = new Dictionary<string, AiUsageModelPrice>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt-5.6-luna"] = new(0.20m, 1.20m)
            }
        }));

        var cost = calculator.Calculate("gpt-5.6-luna", 1_000_000, 1_000_000, 941.1m);
        Require(cost is not null, "Known model must have calculable cost.");
        Require(cost!.EstimatedCostUsd == 1.40m, "Known model USD cost is wrong.");
        Require(cost.EstimatedCostClp == decimal.Round(1.40m * 941.1m, 4), "CLP conversion is wrong.");
        Require(calculator.Calculate("unknown-model", 1000, 1000, 941.1m) is null,
            "Unknown model must not invent cost.");

        var dbPath = Path.Combine(Path.GetTempPath(), $"nexomail-ai-usage-smoke-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<NexoMailDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            await using var database = new NexoMailDbContext(options);
            await database.Database.EnsureCreatedAsync(ct);

            var user = new UserEntity
            {
                Id = Guid.NewGuid(),
                DisplayName = "AI Usage Smoke",
                Email = $"ai-usage-{Guid.NewGuid():N}@nexomail.test",
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true,
                IsEmailVerified = true
            };
            database.Users.Add(user);
            database.AiUsageEvents.Add(new AiUsageEventEntity
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                OccurredAt = DateTimeOffset.UtcNow,
                OperationType = "mail_summary",
                Model = "gpt-5.6-luna",
                InputTokens = 10,
                OutputTokens = 5,
                DurationMs = 25,
                Succeeded = true
            });
            database.AiUsageMonthlySummaries.Add(new AiUsageMonthlySummaryEntity
            {
                UserId = user.Id,
                Year = 2026,
                Month = 9,
                OperationCount = 1,
                SuccessfulOperations = 1,
                InputTokens = 10,
                OutputTokens = 5,
                ActiveDays = 1,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            database.AiUsageSettings.Add(new AiUsageSettingsEntity
            {
                Id = 1,
                GreenMaxClp = 1500m,
                YellowMaxClp = 3000m,
                ReferenceClpPerUsd = 941.1m,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await database.SaveChangesAsync(ct);

            Require(await database.AiUsageEvents.CountAsync(ct) == 1, "Usage event schema must be writable.");
            Require(await database.AiUsageMonthlySummaries.CountAsync(ct) == 1, "Monthly summary schema must be writable.");
            Require(await database.AiUsageSettings.SingleAsync(ct) is not null, "Usage settings schema must be writable.");
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var path = dbPath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
