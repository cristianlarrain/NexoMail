using Microsoft.EntityFrameworkCore;
using NexoMail.Domain;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var ct = CancellationToken.None;
var dbPath = Path.Combine(Path.GetTempPath(), $"nexomail-commercial-smoke-{Guid.NewGuid():N}.db");

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
        DisplayName = "Commercial Smoke",
        Email = $"commercial-smoke-{Guid.NewGuid():N}@nexomail.test",
        PlanCode = CommercialPlanCatalog.Premium,
        CreatedAt = DateTimeOffset.UtcNow,
        IsEmailVerified = true,
        IsActive = true
    };
    database.Users.Add(user);
    await database.SaveChangesAsync(ct);

    var legacy = await CommercialAccessStore.GetAsync(database, user.Id, ct)
        ?? throw new InvalidOperationException("No fue posible resolver el acceso comercial inicial.");

    Ensure(legacy.Subscription.Status == CommercialSubscriptionStatuses.Legacy,
        "Un usuario Premium existente debe iniciar con acceso heredado.");
    Ensure(legacy.PaidAccessActive, "El acceso heredado Premium debe conservar funciones pagadas.");
    Ensure(legacy.EffectivePlan.Code == CommercialPlanCatalog.Premium,
        "El plan efectivo heredado debe seguir siendo Premium.");
    Ensure(legacy.Entitlements.Contains(CommercialEntitlements.NexiAi),
        "Premium debe incluir Nexi e IA.");

    await CommercialSubscriptionMutations.SetPendingCheckoutAsync(
        database,
        user.Id,
        CommercialPlanCatalog.Premium,
        "mercadopago",
        "smoke-pending",
        ct);

    var pending = await CommercialAccessStore.GetAsync(database, user.Id, ct)
        ?? throw new InvalidOperationException("No fue posible resolver el acceso pendiente.");

    Ensure(pending.Subscription.Status == CommercialSubscriptionStatuses.Pending,
        "La contratación iniciada debe quedar pendiente.");
    Ensure(!pending.PaidAccessActive,
        "Una suscripción pendiente no debe habilitar funciones pagadas.");
    Ensure(pending.EffectivePlan.Code == CommercialPlanCatalog.Freemium,
        "Mientras el pago está pendiente, el plan efectivo debe ser Freemium.");
    Ensure(!pending.Entitlements.Contains(CommercialEntitlements.NexiAi),
        "Freemium no debe exponer Nexi e IA.");

    await CommercialSubscriptionMutations.ApplyProviderStateAsync(
        database,
        user.Id,
        CommercialPlanCatalog.Premium,
        CommercialSubscriptionStatuses.Active,
        "mercadopago",
        "smoke-active",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddMonths(1),
        ct);

    var active = await CommercialAccessStore.GetAsync(database, user.Id, ct)
        ?? throw new InvalidOperationException("No fue posible resolver el acceso activo.");

    Ensure(active.PaidAccessActive, "Una suscripción activa debe habilitar funciones pagadas.");
    Ensure(active.EffectivePlan.Code == CommercialPlanCatalog.Premium,
        "Una suscripción Premium activa debe tener Premium como plan efectivo.");
    Ensure(active.Entitlements.Contains(CommercialEntitlements.NexiAi),
        "Premium activo debe habilitar Nexi e IA.");
    Ensure(active.Entitlements.Contains(CommercialEntitlements.AdvancedAnalytics),
        "Premium activo debe habilitar estadísticas avanzadas.");

    await CommercialSubscriptionMutations.ApplyProviderStateAsync(
        database,
        user.Id,
        CommercialPlanCatalog.Premium,
        CommercialSubscriptionStatuses.PastDue,
        "mercadopago",
        "smoke-past-due",
        null,
        null,
        ct);

    var pastDue = await CommercialAccessStore.GetAsync(database, user.Id, ct)
        ?? throw new InvalidOperationException("No fue posible resolver el acceso con pago pendiente.");

    Ensure(!pastDue.PaidAccessActive,
        "Una suscripción con pago pendiente no debe conservar funciones pagadas.");
    Ensure(pastDue.EffectivePlan.Code == CommercialPlanCatalog.Freemium,
        "Una suscripción impaga debe degradar temporalmente a Freemium.");

    await CommercialSubscriptionMutations.ApplyProviderStateAsync(
        database,
        user.Id,
        CommercialPlanCatalog.Premium,
        CommercialSubscriptionStatuses.Canceled,
        "mercadopago",
        "smoke-canceled",
        null,
        null,
        ct);

    var canceled = await CommercialAccessStore.GetAsync(database, user.Id, ct)
        ?? throw new InvalidOperationException("No fue posible resolver el acceso cancelado.");

    Ensure(!canceled.PaidAccessActive,
        "Una suscripción cancelada no debe habilitar funciones pagadas.");
    Ensure(canceled.EffectivePlan.Code == CommercialPlanCatalog.Freemium,
        "Una suscripción cancelada debe quedar con capacidades Freemium.");

    Ensure(MercadoPagoBilling.TryReadExternalReference($"{user.Id:N}:{CommercialPlanCatalog.Premium}", out var parsedUserId, out var parsedPlan),
        "La referencia externa de Mercado Pago no pudo interpretarse.");
    Ensure(parsedUserId == user.Id && parsedPlan == CommercialPlanCatalog.Premium,
        "La referencia externa de Mercado Pago cambió de usuario o plan.");
    Ensure(MercadoPagoBilling.MapStatus("authorized") == CommercialSubscriptionStatuses.Active,
        "Mercado Pago authorized debe mapear a active.");
    Ensure(MercadoPagoBilling.MapStatus("pending") == CommercialSubscriptionStatuses.Pending,
        "Mercado Pago pending debe mapear a pending.");

    Console.WriteLine("PASS: comercial -> FK SQLite -> heredado -> pendiente -> activo -> impago -> cancelado -> entitlements");
}
finally
{
    foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
    {
        var path = dbPath + suffix;
        if (File.Exists(path)) File.Delete(path);
    }
}
