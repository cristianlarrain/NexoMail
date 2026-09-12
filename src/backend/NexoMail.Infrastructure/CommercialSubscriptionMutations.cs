using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure;

public static class CommercialSubscriptionMutations
{
    public static async Task SetPendingCheckoutAsync(
        NexoMailDbContext database,
        Guid userId,
        string targetPlanCode,
        string provider,
        string providerSubscriptionId,
        CancellationToken ct)
    {
        await UpsertAsync(
            database,
            userId,
            targetPlanCode,
            CommercialSubscriptionStatuses.Pending,
            provider,
            providerSubscriptionId,
            null,
            null,
            null,
            ct);
    }

    public static async Task AssignPlanManuallyAsync(
        NexoMailDbContext database,
        Guid userId,
        string planCode,
        CancellationToken ct)
    {
        var normalizedPlanCode = (planCode ?? string.Empty).Trim().ToLowerInvariant();
        var plan = await database.CommercialPlans.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Code == normalizedPlanCode && x.IsActive, ct)
            ?? throw new InvalidOperationException("El plan seleccionado no existe o está inactivo.");
        var user = await database.Users.SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct)
            ?? throw new InvalidOperationException("El usuario seleccionado no existe o está inactivo.");
        if (user.IsOwner)
            throw new InvalidOperationException("El Owner / Administrador general no depende de un plan comercial y no puede ser reasignado.");

        user.PlanCode = plan.Code;
        await database.SaveChangesAsync(ct);

        await UpsertAsync(
            database,
            user.Id,
            plan.Code,
            CommercialSubscriptionStatuses.Active,
            "admin",
            null,
            null,
            null,
            null,
            ct);
    }

    public static async Task GrantTrialAsync(
        NexoMailDbContext database,
        Guid userId,
        string trialType,
        int days,
        CancellationToken ct)
    {
        if (days is < 1 or > 90)
            throw new InvalidOperationException("La prueba debe durar entre 1 y 90 días.");

        var normalizedTrialType = (trialType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedTrialType is not ("premium" or "nexi"))
            throw new InvalidOperationException("El tipo de prueba debe ser Premium o Nexi.");

        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct)
            ?? throw new InvalidOperationException("El usuario seleccionado no existe o está inactivo.");
        if (user.IsOwner)
            throw new InvalidOperationException("El Owner / Administrador general no requiere pruebas temporales.");
        if (!string.Equals(user.PlanCode, CommercialPlanCatalog.Freemium, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Las pruebas temporales sólo se otorgan a usuarios cuyo plan base es Freemium.");

        var trialPlanCode = normalizedTrialType == "premium"
            ? CommercialPlanCatalog.Premium
            : CommercialPlanCatalog.Freemium;

        if (normalizedTrialType == "premium")
        {
            var premiumExists = await database.CommercialPlans.AsNoTracking()
                .AnyAsync(x => x.Code == CommercialPlanCatalog.Premium && x.IsActive, ct);
            if (!premiumExists)
                throw new InvalidOperationException("El plan Premium no está disponible para iniciar una prueba.");
        }

        var now = DateTimeOffset.UtcNow;
        await UpsertAsync(
            database,
            user.Id,
            trialPlanCode,
            CommercialSubscriptionStatuses.Trialing,
            "admin_trial",
            null,
            now,
            null,
            now.AddDays(days),
            ct);
    }

    public static async Task ApplyProviderStateAsync(
        NexoMailDbContext database,
        Guid userId,
        string planCode,
        string status,
        string provider,
        string providerSubscriptionId,
        DateTimeOffset? currentPeriodStart,
        DateTimeOffset? currentPeriodEnd,
        CancellationToken ct)
    {
        var planExists = await database.CommercialPlans.AsNoTracking().AnyAsync(x => x.Code == planCode && x.IsActive, ct);
        if (!planExists) throw new InvalidOperationException("La suscripción hace referencia a un plan que ya no está disponible.");
        var user = await database.Users.SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct)
            ?? throw new InvalidOperationException("La suscripción hace referencia a un usuario inexistente.");

        await UpsertAsync(database, userId, planCode, status, provider, providerSubscriptionId, currentPeriodStart, currentPeriodEnd, null, ct);

        if (!user.IsOwner && CommercialSubscriptionStatuses.GrantsPaidAccess(status) && !string.Equals(user.PlanCode, planCode, StringComparison.OrdinalIgnoreCase))
        {
            user.PlanCode = planCode;
            await database.SaveChangesAsync(ct);
        }
    }

    private static async Task UpsertAsync(
        NexoMailDbContext database,
        Guid userId,
        string planCode,
        string status,
        string provider,
        string? providerSubscriptionId,
        DateTimeOffset? currentPeriodStart,
        DateTimeOffset? currentPeriodEnd,
        DateTimeOffset? trialEndsAt,
        CancellationToken ct)
    {
        var isSqlite = database.Database.IsSqlite();
        var connection = database.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(ct);
        try
        {
            await EnsureSchemaAsync(connection, isSqlite, ct);
            var now = DateTimeOffset.UtcNow;
            var prefix = isSqlite ? "$" : "@";
            await using var command = connection.CreateCommand();
            command.CommandText = BuildUpsertCommandText(isSqlite);
            AddParameter(command, $"{prefix}userId", userId);
            AddParameter(command, $"{prefix}planCode", planCode);
            AddParameter(command, $"{prefix}status", status);
            AddParameter(command, $"{prefix}provider", provider);
            AddParameter(command, $"{prefix}providerSubscriptionId", providerSubscriptionId);
            AddParameter(command, $"{prefix}periodStart", isSqlite ? currentPeriodStart?.ToString("O") : currentPeriodStart);
            AddParameter(command, $"{prefix}periodEnd", isSqlite ? currentPeriodEnd?.ToString("O") : currentPeriodEnd);
            AddParameter(command, $"{prefix}trialEndsAt", isSqlite ? trialEndsAt?.ToString("O") : trialEndsAt);
            AddParameter(command, $"{prefix}canceledAt",
                status == CommercialSubscriptionStatuses.Canceled
                    ? isSqlite ? now.ToString("O") : now
                    : null);
            AddParameter(command, $"{prefix}paymentDueAt",
                status == CommercialSubscriptionStatuses.PastDue
                    ? isSqlite ? now.ToString("O") : now
                    : null);
            AddParameter(command, $"{prefix}createdAt", isSqlite ? now.ToString("O") : now);
            AddParameter(command, $"{prefix}updatedAt", isSqlite ? now.ToString("O") : now);
            await command.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static string BuildUpsertCommandText(bool isSqlite) => isSqlite
        ? """
            INSERT INTO CommercialSubscriptions
            (UserId, PlanCode, Status, Provider, ProviderCustomerId, ProviderSubscriptionId,
             CurrentPeriodStart, CurrentPeriodEnd, TrialEndsAt, CancelAtPeriodEnd, CanceledAt, PaymentDueAt, CreatedAt, UpdatedAt)
            VALUES
            ($userId, $planCode, $status, $provider, NULL, $providerSubscriptionId,
             $periodStart, $periodEnd, $trialEndsAt, 0, $canceledAt, $paymentDueAt, $createdAt, $updatedAt)
            ON CONFLICT(UserId) DO UPDATE SET
                PlanCode = excluded.PlanCode,
                Status = excluded.Status,
                Provider = excluded.Provider,
                ProviderCustomerId = NULL,
                ProviderSubscriptionId = excluded.ProviderSubscriptionId,
                CurrentPeriodStart = excluded.CurrentPeriodStart,
                CurrentPeriodEnd = excluded.CurrentPeriodEnd,
                TrialEndsAt = excluded.TrialEndsAt,
                CancelAtPeriodEnd = 0,
                CanceledAt = excluded.CanceledAt,
                PaymentDueAt = excluded.PaymentDueAt,
                UpdatedAt = excluded.UpdatedAt;
            """
        : """
            IF EXISTS (SELECT 1 FROM [CommercialSubscriptions] WHERE [UserId] = @userId)
            BEGIN
                UPDATE [CommercialSubscriptions] SET
                    [PlanCode] = @planCode,
                    [Status] = @status,
                    [Provider] = @provider,
                    [ProviderCustomerId] = NULL,
                    [ProviderSubscriptionId] = @providerSubscriptionId,
                    [CurrentPeriodStart] = @periodStart,
                    [CurrentPeriodEnd] = @periodEnd,
                    [TrialEndsAt] = @trialEndsAt,
                    [CancelAtPeriodEnd] = CAST(0 AS bit),
                    [CanceledAt] = @canceledAt,
                    [PaymentDueAt] = @paymentDueAt,
                    [UpdatedAt] = @updatedAt
                WHERE [UserId] = @userId;
            END
            ELSE
            BEGIN
                INSERT INTO [CommercialSubscriptions]
                ([UserId], [PlanCode], [Status], [Provider], [ProviderCustomerId], [ProviderSubscriptionId],
                 [CurrentPeriodStart], [CurrentPeriodEnd], [TrialEndsAt], [CancelAtPeriodEnd],
                 [CanceledAt], [PaymentDueAt], [CreatedAt], [UpdatedAt])
                VALUES
                (@userId, @planCode, @status, @provider, NULL, @providerSubscriptionId,
                 @periodStart, @periodEnd, @trialEndsAt, CAST(0 AS bit),
                 @canceledAt, @paymentDueAt, @createdAt, @updatedAt);
            END;
            """;

    private static async Task EnsureSchemaAsync(DbConnection connection, bool isSqlite, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = isSqlite
            ? """
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
                """
            : """
                IF OBJECT_ID(N'CommercialSubscriptions', N'U') IS NULL
                BEGIN
                    CREATE TABLE [CommercialSubscriptions] (
                        [UserId] uniqueidentifier NOT NULL CONSTRAINT [PK_CommercialSubscriptions] PRIMARY KEY,
                        [PlanCode] nvarchar(32) NOT NULL,
                        [Status] nvarchar(32) NOT NULL,
                        [Provider] nvarchar(64) NULL,
                        [ProviderCustomerId] nvarchar(256) NULL,
                        [ProviderSubscriptionId] nvarchar(256) NULL,
                        [CurrentPeriodStart] datetimeoffset NULL,
                        [CurrentPeriodEnd] datetimeoffset NULL,
                        [TrialEndsAt] datetimeoffset NULL,
                        [CancelAtPeriodEnd] bit NOT NULL CONSTRAINT [DF_CommercialSubscriptions_CancelAtPeriodEnd] DEFAULT CAST(0 AS bit),
                        [CanceledAt] datetimeoffset NULL,
                        [PaymentDueAt] datetimeoffset NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        [UpdatedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [FK_CommercialSubscriptions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
                    );
                END;
                """;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
