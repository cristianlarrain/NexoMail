using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using NexoMail.Domain;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;

internal static class AiUsageAdminSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync(CancellationToken.None).GetAwaiter().GetResult();

    private static async Task RunAsync(CancellationToken ct)
    {
        await VerifyExistingDatabaseBootstrapAsync(ct);
        await VerifyAnalyticsProjectionSettingsAndRetentionAsync(ct);
    }

    private static async Task VerifyExistingDatabaseBootstrapAsync(CancellationToken ct)
    {
        var dbPath = TempDbPath();
        try
        {
            await using var database = CreateDatabase(dbPath);
            var connection = database.Database.GetDbConnection();
            await connection.OpenAsync(ct);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "CREATE TABLE Users (Id TEXT NOT NULL PRIMARY KEY, DisplayName TEXT NOT NULL, Email TEXT NOT NULL, PlanCode TEXT NOT NULL DEFAULT 'freemium', CreatedAt TEXT NOT NULL, IsActive INTEGER NOT NULL DEFAULT 1, IsEmailVerified INTEGER NOT NULL DEFAULT 1, IsAdministrator INTEGER NOT NULL DEFAULT 0, IsOwner INTEGER NOT NULL DEFAULT 0);";
                await command.ExecuteNonQueryAsync(ct);
            }

            await AiUsageSchemaBootstrap.EnsureAsync(database, ct);
            Require(await TableExistsAsync(connection, "AiUsageEvents", ct), "Existing databases must receive AiUsageEvents.");
            Require(await TableExistsAsync(connection, "AiUsageMonthlySummaries", ct), "Existing databases must receive AiUsageMonthlySummaries.");
            Require(await TableExistsAsync(connection, "AiUsageSettings", ct), "Existing databases must receive AiUsageSettings.");
        }
        finally { DeleteDb(dbPath); }
    }

    private static async Task VerifyAnalyticsProjectionSettingsAndRetentionAsync(CancellationToken ct)
    {
        var now = new DateTimeOffset(2026, 9, 11, 15, 0, 0, TimeSpan.Zero);
        var dbPath = TempDbPath();
        try
        {
            await using var database = CreateDatabase(dbPath);
            await database.Database.EnsureCreatedAsync(ct);
            await AiUsageSchemaBootstrap.EnsureAsync(database, ct);

            var trialUser = NewUser("trial@nexomail.test");
            var regularUser = NewUser("regular@nexomail.test");
            database.Users.AddRange(trialUser, regularUser);
            database.CommercialPlans.AddRange(
                Plan(CommercialPlanCatalog.Freemium, "Freemium"),
                Plan(CommercialPlanCatalog.Premium, "Premium", featured: true));
            await database.SaveChangesAsync(ct);

            await CommercialSubscriptionMutations.GrantTrialAsync(database, trialUser.Id, "premium", 30, ct);
            var trialStart = now.AddDays(-10);
            var trialEnd = trialStart.AddDays(30);
            await OverrideTrialDatesAsync(database.Database.GetDbConnection(), trialUser.Id, trialStart, trialEnd, ct);

            SeedEvent(database, trialUser.Id, now.AddDays(-1), "mail_summary", 500m);
            SeedEvent(database, trialUser.Id, now.AddDays(-3), "mail_report", 700m);
            SeedEvent(database, trialUser.Id, now.AddDays(-8), "mail_summary", 300m);
            SeedEvent(database, regularUser.Id, now.AddDays(-2), "writing_assistant", 400m);
            SeedEvent(database, regularUser.Id, now.AddDays(-9), "search_interpretation", 200m);
            SeedEvent(database, trialUser.Id, now.AddMonths(-13), "mail_summary", 999m);
            database.AiUsageMonthlySummaries.Add(new AiUsageMonthlySummaryEntity
            {
                UserId = trialUser.Id,
                Year = now.AddMonths(-13).Year,
                Month = now.AddMonths(-13).Month,
                OperationCount = 1,
                SuccessfulOperations = 1,
                InputTokens = 100,
                OutputTokens = 50,
                EstimatedCostUsd = 999m / 941.1m,
                EstimatedCostClp = 999m,
                ActiveDays = 1,
                UpdatedAt = now.AddMonths(-13)
            });
            await database.SaveChangesAsync(ct);

            var service = new AiUsageAdminService(database, new FixedTimeProvider(now));
            var week = await service.GetSummaryAsync(AiUsagePeriod.Week, ct);
            Require(week.CurrentCostClp == 1600m, "Current week cost must include both active users.");
            Require(week.PreviousCostClp == 500m, "Previous week cost must be calculated separately.");
            Require(week.Operations == 3, "Current week operation count is wrong.");
            Require(week.ActiveUsers == 2, "Current week active-user count is wrong.");
            Require(week.AverageCostPerActiveUserClp == 800m, "Average current-week cost is wrong.");
            Require(week.VariationPercent == 220m, "Weekly variation percentage is wrong.");

            var month = await service.GetSummaryAsync(AiUsagePeriod.Month, ct);
            Require(month.CurrentCostClp == 2100m, "Current month total is wrong.");

            var users = await service.GetUsersAsync("projected", ct);
            var trial = users.Single(x => x.UserId == trialUser.Id);
            Require(trial.IsTrialActive, "Admin trial must be detected.");
            Require(trial.AccumulatedCostClp == 1500m, "Trial accumulated cost must start at trial start.");
            Require(trial.ProjectedCostClp == 4500m, "30-day calendar projection is wrong.");
            Require(!trial.IsInitialProjection, "Ten-day trial must not be marked as an initial projection.");
            Require(trial.SevenDayProjectedCostClp == decimal.Round(1200m / 7m * 30m, 2), "Seven-day projection is wrong.");
            Require(trial.CostStatus == "rojo", "Configured semaphore classification is wrong.");

            var detail = await service.GetUserAsync(trialUser.Id, ct);
            Require(detail is not null && detail.Daily.Count > 0, "User detail must expose 30-day daily usage.");
            Require(detail!.Operations.Any(x => x.OperationType == "mail_summary"), "User detail must group operation types.");

            await RequireThrowsAsync<InvalidOperationException>(() =>
                service.UpdateSettingsAsync(3000m, 1500m, 941.1m, ct));
            await service.UpdateSettingsAsync(2000m, 5000m, 950m, ct);
            var settings = await service.GetSettingsAsync(ct);
            Require(settings.GreenMaxClp == 2000m && settings.YellowMaxClp == 5000m && settings.ReferenceClpPerUsd == 950m,
                "Owner usage settings were not persisted.");

            var retention = new AiUsageRetentionService(database);
            var beforeSummaries = await database.AiUsageMonthlySummaries.CountAsync(ct);
            Require(beforeSummaries == 1, "Retention smoke must include a preserved historical monthly summary.");
            var deleted = await retention.DeleteExpiredDetailAsync(now, ct);
            Require(deleted == 1, "Retention must delete only detail older than 12 months.");
            Require(await database.AiUsageMonthlySummaries.CountAsync(ct) == beforeSummaries,
                "Retention must never delete monthly summaries.");
        }
        finally { DeleteDb(dbPath); }
    }

    private static void SeedEvent(NexoMailDbContext database, Guid userId, DateTimeOffset occurredAt, string operationType, decimal costClp)
    {
        database.AiUsageEvents.Add(new AiUsageEventEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OccurredAt = occurredAt.UtcDateTime,
            OperationType = operationType,
            Model = "gpt-5.6-luna",
            InputTokens = 100,
            OutputTokens = 50,
            DurationMs = 20,
            Succeeded = true,
            EstimatedCostUsd = costClp / 941.1m,
            EstimatedCostClp = costClp,
            ClpPerUsd = 941.1m
        });
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
            AddParameter(command, "$userId", userId.ToString());
            await command.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string name, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        AddParameter(command, "$name", name);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 1;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
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

    private static UserEntity NewUser(string email) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = email.Split('@')[0],
        Email = email,
        PlanCode = CommercialPlanCatalog.Freemium,
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true,
        IsEmailVerified = true
    };

    private static NexoMailDbContext CreateDatabase(string path) => new(
        new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite($"Data Source={path}").Options);

    private static string TempDbPath() => Path.Combine(Path.GetTempPath(), $"nexomail-ai-admin-smoke-{Guid.NewGuid():N}.db");

    private static void DeleteDb(string dbPath)
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = dbPath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static async Task RequireThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
            throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
        }
        catch (TException)
        {
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
