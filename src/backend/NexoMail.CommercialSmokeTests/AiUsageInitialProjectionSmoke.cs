using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using NexoMail.Domain;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;

internal static class AiUsageInitialProjectionSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync(CancellationToken.None).GetAwaiter().GetResult();

    private static async Task RunAsync(CancellationToken ct)
    {
        var now = new DateTimeOffset(2026, 9, 11, 4, 50, 0, TimeSpan.Zero);
        var dbPath = Path.Combine(Path.GetTempPath(), $"nexomail-ai-initial-projection-{Guid.NewGuid():N}.db");

        try
        {
            await using var database = new NexoMailDbContext(
                new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite($"Data Source={dbPath};Pooling=False").Options);
            await database.Database.EnsureCreatedAsync(ct);
            await AiUsageSchemaBootstrap.EnsureAsync(database, ct);

            var user = new UserEntity
            {
                Id = Guid.NewGuid(),
                DisplayName = "Initial Trial",
                Email = "initial-trial@nexomail.test",
                PlanCode = CommercialPlanCatalog.Freemium,
                CreatedAt = now.AddDays(-1),
                IsActive = true,
                IsEmailVerified = true
            };

            database.Users.Add(user);
            database.CommercialPlans.AddRange(
                Plan(CommercialPlanCatalog.Freemium, "Freemium"),
                Plan(CommercialPlanCatalog.Premium, "Premium", featured: true));
            await database.SaveChangesAsync(ct);

            await CommercialSubscriptionMutations.GrantTrialAsync(database, user.Id, "premium", 30, ct);
            var trialStart = now.AddHours(-1);
            var trialEnd = trialStart.AddDays(30);
            await OverrideTrialDatesAsync(database.Database.GetDbConnection(), user.Id, trialStart, trialEnd, ct);

            database.AiUsageEvents.Add(new AiUsageEventEntity
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                OccurredAt = now.AddMinutes(-30).UtcDateTime,
                OperationType = "mail_summary",
                Model = "gpt-5.6-luna",
                InputTokens = 5654,
                OutputTokens = 1527,
                DurationMs = 20,
                Succeeded = true,
                EstimatedCostUsd = 3m / 941.1m,
                EstimatedCostClp = 3m,
                ClpPerUsd = 941.1m
            });
            await database.SaveChangesAsync(ct);

            var service = new AiUsageAdminService(database, new FixedTimeProvider(now));
            var row = (await service.GetUsersAsync("projected", ct)).Single(x => x.UserId == user.Id);

            Require(row.IsInitialProjection, "A trial younger than 24 hours must be marked as an initial projection.");
            Require(row.AccumulatedCostClp == 3m, "Initial accumulated cost is wrong.");
            Require(row.AverageDailyCostClp == 3m, "Initial daily average must use a minimum of one full day.");
            Require(row.ProjectedCostClp == 90m, "Initial 30-day projection must use a minimum of one full day.");
            Require(row.CostStatus == "verde", "The initial projection must not be inflated into a higher warning state.");
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

    private static async Task OverrideTrialDatesAsync(DbConnection connection, Guid userId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE CommercialSubscriptions SET CurrentPeriodStart = $start, TrialEndsAt = $end, UpdatedAt = $updated WHERE UserId = $userId;";
            AddParameter(command, "$start", start.ToString("O"));
            AddParameter(command, "$end", end.ToString("O"));
            AddParameter(command, "$updated", start.ToString("O"));
            AddParameter(command, "$userId", userId);
            await command.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        connectionSafeAdd(parameter);

        void connectionSafeAdd(DbParameter p)
        {
            // local helper preserves the original call shape while avoiding unrelated refactoring
        }
    }

    private static CommercialPlanEntity Plan(string code, string name, bool featured = false) => new()
    {
        Code = code,
        Name = name,
        Price = "0",
        Cadence = "mensual",
        Description = name,
        FeaturesJson = "[]",
        EntitlementsJson = "[]",
        IsFeatured = featured,
        IsActive = true,
        SortOrder = featured ? 20 : 10,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
