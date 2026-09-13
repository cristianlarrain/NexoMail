using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using NexoMail.Domain;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;

internal static class WelcomeTrialSmoke
{
    [ModuleInitializer]
    internal static void Verify() => VerifyAsync().GetAwaiter().GetResult();

    private static async Task VerifyAsync()
    {
        var ct = CancellationToken.None;
        var dbPath = Path.Combine(Path.GetTempPath(), $"nexomail-welcome-trial-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<NexoMailDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            await using var database = new NexoMailDbContext(options);
            await database.Database.EnsureCreatedAsync(ct);
            await DatabaseBootstrap.EnsureAuthenticationSchemaAsync(database, ct);

            var user = new UserEntity
            {
                Id = Guid.NewGuid(),
                DisplayName = "Welcome Trial",
                Email = $"welcome-{Guid.NewGuid():N}@nexomail.test",
                PlanCode = CommercialPlanCatalog.Freemium,
                CreatedAt = DateTimeOffset.UtcNow,
                IsEmailVerified = true,
                IsActive = true
            };
            database.Users.Add(user);
            await database.SaveChangesAsync(ct);

            var method = typeof(CommercialSubscriptionMutations).GetMethod("GrantWelcomeTrialAsync");
            Ensure(method is not null, "Debe existir la concesión idempotente de Premium de bienvenida.");

            await (Task)method!.Invoke(null, [database, user.Id, 30, ct])!;
            var first = await CommercialAccessStore.GetAsync(database, user.Id, ct)
                ?? throw new InvalidOperationException("No fue posible resolver Premium de bienvenida.");
            Ensure(first.EffectivePlan.Code == CommercialPlanCatalog.Premium, "La bienvenida debe activar Premium.");
            Ensure(first.Subscription.Provider == "welcome_trial", "La bienvenida debe usar un proveedor distinguible.");
            Ensure(first.Subscription.TrialEndsAt > DateTimeOffset.UtcNow.AddDays(29), "La bienvenida debe durar 30 días.");

            var originalEnd = first.Subscription.TrialEndsAt;
            await (Task)method.Invoke(null, [database, user.Id, 30, ct])!;
            var repeated = await CommercialAccessStore.GetAsync(database, user.Id, ct)
                ?? throw new InvalidOperationException("No fue posible comprobar idempotencia.");
            Ensure(repeated.Subscription.TrialEndsAt == originalEnd, "Repetir la operación no debe extender la prueba.");

            await CommercialSubscriptionMutations.ApplyProviderStateAsync(
                database, user.Id, CommercialPlanCatalog.Premium, CommercialSubscriptionStatuses.Active,
                "mercadopago", "paid-subscription", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1), ct);
            await (Task)method.Invoke(null, [database, user.Id, 30, ct])!;
            var paid = await CommercialAccessStore.GetAsync(database, user.Id, ct)
                ?? throw new InvalidOperationException("No fue posible comprobar la suscripción pagada.");
            Ensure(paid.Subscription.Provider == "mercadopago", "La bienvenida no debe reemplazar una suscripción pagada.");
            Ensure(paid.Subscription.Status == CommercialSubscriptionStatuses.Active, "La suscripción pagada debe seguir activa.");
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
