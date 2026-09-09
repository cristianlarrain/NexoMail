using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure;

public sealed record CommercialSubscriptionState(
    string Status,
    string? Provider,
    string? ProviderCustomerId,
    string? ProviderSubscriptionId,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? TrialEndsAt,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CanceledAt,
    DateTimeOffset? PaymentDueAt,
    DateTimeOffset UpdatedAt)
{
    public string PlanCode { get; init; } = CommercialPlanCatalog.Freemium;
}

public sealed record CommercialAccessSnapshot(
    CommercialPlanEntity AssignedPlan,
    CommercialPlanEntity EffectivePlan,
    CommercialSubscriptionState Subscription,
    IReadOnlyList<string> Entitlements,
    bool PaidAccessActive);

public static class CommercialAccessStore
{
    public static async Task<CommercialAccessSnapshot?> GetAsync(NexoMailDbContext database, Guid userId, CancellationToken ct = default)
    {
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
        if (user is null) return null;

        var plans = await database.CommercialPlans.AsNoTracking().Where(x => x.IsActive).ToArrayAsync(ct);
        var freemium = plans.FirstOrDefault(x => x.Code == CommercialPlanCatalog.Freemium)
            ?? throw new InvalidOperationException("No existe un plan Freemium activo.");
        var assigned = plans.FirstOrDefault(x => x.Code == user.PlanCode) ?? freemium;
        var subscription = await EnsureSubscriptionAsync(database, userId, assigned.Code, ct);

        var isFree = string.Equals(assigned.Code, CommercialPlanCatalog.Freemium, StringComparison.OrdinalIgnoreCase);
        var subscriptionMatchesPlan = string.Equals(subscription.PlanCode, assigned.Code, StringComparison.OrdinalIgnoreCase);
        var paidAccessActive = isFree || subscriptionMatchesPlan && CommercialSubscriptionStatuses.GrantsPaidAccess(subscription.Status);
        var effective = paidAccessActive ? assigned : freemium;
        return new CommercialAccessSnapshot(assigned, effective, subscription, EntitlementsFor(effective), paidAccessActive);
    }

    public static IReadOnlyList<string> EntitlementsFor(CommercialPlanEntity plan)
    {
        var profile = plan.IsWhiteLabel || string.Equals(plan.Code, CommercialPlanCatalog.WhiteLabel, StringComparison.OrdinalIgnoreCase)
            ? CommercialPlanCatalog.WhiteLabel
            : plan.IsCorporate || string.Equals(plan.Code, CommercialPlanCatalog.Corporate, StringComparison.OrdinalIgnoreCase)
                ? CommercialPlanCatalog.Corporate
                : plan.IsFeatured || string.Equals(plan.Code, CommercialPlanCatalog.Premium, StringComparison.OrdinalIgnoreCase)
                    ? CommercialPlanCatalog.Premium
                    : CommercialPlanCatalog.Freemium;
        return CommercialEntitlements.DefaultsForPlan(profile);
    }

    public static async Task<bool> HasEntitlementAsync(NexoMailDbContext database, Guid userId, string entitlement, CancellationToken ct = default)
    {
        var access = await GetAsync(database, userId, ct);
        return access?.Entitlements.Contains(entitlement, StringComparer.OrdinalIgnoreCase) == true;
    }

    private static async Task<CommercialSubscriptionState> EnsureSubscriptionAsync(NexoMailDbContext database, Guid userId, string planCode, CancellationToken ct)
    {
        var connection = database.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(ct);
        try
        {
            await EnsureSchemaAsync(connection, ct);
            var existing = await ReadAsync(connection, userId, ct);
            if (existing is not null) return existing;

            var now = DateTimeOffset.UtcNow;
            var status = string.Equals(planCode, CommercialPlanCatalog.Freemium, StringComparison.OrdinalIgnoreCase)
                ? CommercialSubscriptionStatuses.Active
                : CommercialSubscriptionStatuses.Legacy;
            await using var insert = connection.CreateCommand();
            insert.CommandText = @"
                INSERT INTO CommercialSubscriptions
                (UserId, PlanCode, Status, Provider, ProviderCustomerId, ProviderSubscriptionId,
                 CurrentPeriodStart, CurrentPeriodEnd, TrialEndsAt, CancelAtPeriodEnd, CanceledAt, PaymentDueAt, CreatedAt, UpdatedAt)
                VALUES
                ($userId, $planCode, $status, NULL, NULL, NULL, NULL, NULL, NULL, 0, NULL, NULL, $createdAt, $updatedAt);";
            AddParameter(insert, "$userId", userId);
            AddParameter(insert, "$planCode", planCode);
            AddParameter(insert, "$status", status);
            AddParameter(insert, "$createdAt", now.ToString("O"));
            AddParameter(insert, "$updatedAt", now.ToString("O"));
            await insert.ExecuteNonQueryAsync(ct);
            return new(status, null, null, null, null, null, null, false, null, null, now) { PlanCode = planCode };
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static async Task EnsureSchemaAsync(DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS CommercialSubscriptions (
                UserId TEXT NOT NULL CONSTRAINT PK_CommercialSubscriptions PRIMARY KEY,
                PlanCode TEXT NOT NULL,
                Status TEXT NOT NULL,
                Provider TEXT NULL,
                ProviderCustomerId TEXT NULL,
                ProviderSubscriptionId TEXT NULL,
                CurrentPeriodStart TEXT NULL,
                CurrentPeriodEnd TEXT NULL,
                TrialEndsAt TEXT NULL,
                CancelAtPeriodEnd INTEGER NOT NULL DEFAULT 0,
                CanceledAt TEXT NULL,
                PaymentDueAt TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                CONSTRAINT FK_CommercialSubscriptions_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_CommercialSubscriptions_Status ON CommercialSubscriptions (Status);
            CREATE INDEX IF NOT EXISTS IX_CommercialSubscriptions_ProviderSubscriptionId ON CommercialSubscriptions (ProviderSubscriptionId);";
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<CommercialSubscriptionState?> ReadAsync(DbConnection connection, Guid userId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT PlanCode, Status, Provider, ProviderCustomerId, ProviderSubscriptionId, CurrentPeriodStart, CurrentPeriodEnd,
                   TrialEndsAt, CancelAtPeriodEnd, CanceledAt, PaymentDueAt, UpdatedAt
            FROM CommercialSubscriptions WHERE UserId = $userId LIMIT 1;";
        AddParameter(command, "$userId", userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new CommercialSubscriptionState(
            reader.GetString(1),
            ReadNullableString(reader, 2),
            ReadNullableString(reader, 3),
            ReadNullableString(reader, 4),
            ReadNullableDate(reader, 5),
            ReadNullableDate(reader, 6),
            ReadNullableDate(reader, 7),
            reader.GetInt32(8) != 0,
            ReadNullableDate(reader, 9),
            ReadNullableDate(reader, 10),
            DateTimeOffset.Parse(reader.GetString(11)))
        {
            PlanCode = reader.GetString(0)
        };
    }

    private static string? ReadNullableString(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static DateTimeOffset? ReadNullableDate(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : DateTimeOffset.Parse(reader.GetString(ordinal));

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
