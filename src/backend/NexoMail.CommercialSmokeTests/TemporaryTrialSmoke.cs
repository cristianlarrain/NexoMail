using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using NexoMail.Domain;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;

internal static class TemporaryTrialSmoke
{
    [ModuleInitializer]
    internal static void Verify() => VerifyAsync().GetAwaiter().GetResult();

    private static async Task VerifyAsync()
    {
        var ct = CancellationToken.None;
        var dbPath = Path.Combine(Path.GetTempPath(), $"nexomail-trial-smoke-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<NexoMailDbContext>()
                .UseSqlite($"Data Source={dbPath};Pooling=False")
                .Options;

            await using var database = new NexoMailDbContext(options);
            await database.Database.EnsureCreatedAsync(ct);
            await DatabaseBootstrap.EnsureAuthenticationSchemaAsync(database, ct);

            var user = new UserEntity
            {
                Id = Guid.NewGuid(),
                DisplayName = "Trial Smoke",
                Email = $"trial-smoke-{Guid.NewGuid():N}@nexomail.test",
                PlanCode = CommercialPlanCatalog.Freemium,
                CreatedAt = DateTimeOffset.UtcNow,
                IsEmailVerified = true,
                IsActive = true
            };
            database.Users.Add(user);
            await database.SaveChangesAsync(ct);

            var grantTrialMethod = typeof(CommercialSubscriptionMutations).GetMethod("GrantTrialAsync");
            Ensure(grantTrialMethod is not null,
                "Debe existir una operación administrativa para otorgar pruebas temporales.");

            var premiumTrialTask = grantTrialMethod!.Invoke(null, [database, user.Id, "premium", 30, ct]) as Task;
            Ensure(premiumTrialTask is not null, "La prueba Premium debe iniciarse de forma asíncrona.");
            await premiumTrialTask!;

            await database.Entry(user).ReloadAsync(ct);
            var premiumTrial = await CommercialAccessStore.GetAsync(database, user.Id, ct)
                ?? throw new InvalidOperationException("No fue posible resolver la prueba Premium.");

            Ensure(user.PlanCode == CommercialPlanCatalog.Freemium,
                "La prueba temporal no debe modificar el plan base Freemium del usuario.");
            Ensure(premiumTrial.Subscription.Status == CommercialSubscriptionStatuses.Trialing,
                "Una prueba vigente debe exponerse con estado trialing.");
            Ensure(string.Equals(premiumTrial.Subscription.Provider, "admin_trial", StringComparison.OrdinalIgnoreCase),
                "La prueba debe quedar identificada como otorgada por administración.");
            Ensure(premiumTrial.Subscription.TrialEndsAt is not null && premiumTrial.Subscription.TrialEndsAt > DateTimeOffset.UtcNow.AddDays(29),
                "La prueba Premium de 30 días debe registrar su fecha de término.");
            Ensure(premiumTrial.EffectivePlan.Code == CommercialPlanCatalog.Premium,
                "Durante una prueba Premium el plan efectivo debe ser Premium.");
            Ensure(premiumTrial.Entitlements.Contains(CommercialEntitlements.NexiAi) && premiumTrial.Entitlements.Contains(CommercialEntitlements.AdvancedAnalytics),
                "La prueba Premium debe habilitar las capacidades Premium configuradas.");

            await database.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE CommercialSubscriptions SET TrialEndsAt = {DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O")} WHERE UserId = {user.Id}",
                ct);

            var expiredTrial = await CommercialAccessStore.GetAsync(database, user.Id, ct)
                ?? throw new InvalidOperationException("No fue posible resolver la prueba vencida.");
            Ensure(expiredTrial.Subscription.Status == CommercialSubscriptionStatuses.Expired,
                "Una prueba vencida debe exponerse como expired.");
            Ensure(expiredTrial.EffectivePlan.Code == CommercialPlanCatalog.Freemium,
                "Al vencer la prueba Premium el usuario debe volver automáticamente a Freemium.");
            Ensure(!expiredTrial.Entitlements.Contains(CommercialEntitlements.NexiAi),
                "Al vencer la prueba Premium deben retirarse las capacidades Premium.");

            var nexiTrialTask = grantTrialMethod.Invoke(null, [database, user.Id, "nexi", 15, ct]) as Task;
            Ensure(nexiTrialTask is not null, "La prueba Nexi debe iniciarse de forma asíncrona.");
            await nexiTrialTask!;

            var nexiTrial = await CommercialAccessStore.GetAsync(database, user.Id, ct)
                ?? throw new InvalidOperationException("No fue posible resolver la prueba de Nexi.");
            Ensure(nexiTrial.EffectivePlan.Code == CommercialPlanCatalog.Freemium,
                "Una prueba sólo de Nexi debe conservar Freemium como plan efectivo.");
            Ensure(nexiTrial.Entitlements.Contains(CommercialEntitlements.NexiAi),
                "La prueba de Nexi debe habilitar Nexi e IA.");
            Ensure(!nexiTrial.Entitlements.Contains(CommercialEntitlements.AdvancedAnalytics),
                "La prueba sólo de Nexi no debe regalar todas las funciones Premium.");
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
