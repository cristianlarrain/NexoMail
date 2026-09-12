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
        var connection = database.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(ct);
        try
        {
            await EnsureSchemaAsync(connection, ct);
            var now = DateTimeOffset.UtcNow;
            await using var command = connection.CreateCommand();
            command.CommandText = @"
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
                    UpdatedAt = excluded.UpdatedAt;";
            AddParameter(command, "$userId", userId);
            AddParameter(command, "$planCode", planCode);
            AddParameter(command, "$status", status);
            AddParameter(command, "$provider", provider);
            AddParameter(command, "$providerSubscriptionId", providerSubscriptionId);
            AddParameter(command, "$periodStart", currentPeriodStart?.ToString("O"));
            AddParameter(command, "$periodEnd", currentPeriodEnd?.ToString("O"));
            AddParameter(command, "$trialEndsAt", trialEndsAt?.ToString("O"));
            AddParameter(command, "$canceledAt", status == CommercialSubscriptionStatuses.Canceled ? now.ToString("O") : null);
            AddParameter(command, "$paymentDueAt", status == CommercialSubscriptionStatuses.PastDue ? now.ToString("O") : null);
            AddParameter(command, "$createdAt", now.ToString("O"));
            AddParameter(command, "$updatedAt", now.ToString("O"));
            await command.ExecuteNonQueryAsync(ct);
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
            );";
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
