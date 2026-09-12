using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using NexoMail.Domain;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;

internal static class OwnerAccessSmoke
{
    [ModuleInitializer]
    internal static void Verify() => VerifyAsync().GetAwaiter().GetResult();

    private static async Task VerifyAsync()
    {
        var ct = CancellationToken.None;
        var dbPath = Path.Combine(Path.GetTempPath(), $"nexomail-owner-smoke-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<NexoMailDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using var database = new NexoMailDbContext(options);
            await database.Database.EnsureCreatedAsync(ct);

            var administrator = new UserEntity
            {
                Id = Guid.NewGuid(),
                DisplayName = "Owner Smoke",
                Email = $"owner-smoke-{Guid.NewGuid():N}@nexomail.test",
                PlanCode = CommercialPlanCatalog.Freemium,
                IsAdministrator = true,
                CreatedAt = DateTimeOffset.UtcNow,
                IsEmailVerified = true,
                IsActive = true
            };
            database.Users.Add(administrator);
            await database.SaveChangesAsync(ct);

            await DatabaseBootstrap.EnsureAuthenticationSchemaAsync(database, ct);
            await database.Entry(administrator).ReloadAsync(ct);

            var ownerProperty = typeof(UserEntity).GetProperty("IsOwner");
            Ensure(ownerProperty is not null, "UserEntity debe distinguir al Owner del administrador comercial.");
            Ensure(ownerProperty!.GetValue(administrator) as bool? == true,
                "El administrador existente debe migrarse automáticamente a Owner cuando todavía no exista uno.");

            var access = await CommercialAccessStore.GetAsync(database, administrator.Id, ct)
                ?? throw new InvalidOperationException("No fue posible resolver el acceso del Owner.");
            Ensure(access.PaidAccessActive, "El Owner debe conservar acceso total sin depender del estado de cobro.");
            Ensure(access.EffectivePlan.Code == "owner", "El acceso efectivo del Owner debe identificarse como interno y no como un plan comercial.");
            Ensure(access.EffectivePlan.MaxAccounts is null, "El Owner no debe quedar sujeto al límite comercial de cuentas.");
            Ensure(CommercialEntitlements.Definitions.All(definition => access.Entitlements.Contains(definition.Code)),
                "El Owner debe disponer de todas las capacidades de NexoMail.");

            await CommercialSubscriptionMutations.SetPendingCheckoutAsync(
                database,
                administrator.Id,
                CommercialPlanCatalog.Premium,
                "mercadopago",
                "owner-pending-smoke",
                ct);

            var pendingAccess = await CommercialAccessStore.GetAsync(database, administrator.Id, ct)
                ?? throw new InvalidOperationException("No fue posible resolver el acceso del Owner con cobro pendiente.");
            Ensure(pendingAccess.PaidAccessActive && pendingAccess.EffectivePlan.Code == "owner",
                "Un cobro pendiente no debe degradar al Owner.");
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

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
