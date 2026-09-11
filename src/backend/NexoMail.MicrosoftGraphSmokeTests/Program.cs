using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;

var databasePath = Path.Combine(Path.GetTempPath(), $"nexomail-msgraph-{Guid.NewGuid():N}.db");
try
{
    var options = new DbContextOptionsBuilder<NexoMailDbContext>()
        .UseSqlite($"Data Source={databasePath}")
        .Options;

    await using var database = new NexoMailDbContext(options);
    await database.Database.EnsureCreatedAsync();

    var userId = Guid.NewGuid();
    database.Users.Add(new UserEntity
    {
        Id = userId,
        DisplayName = "Microsoft Graph Smoke",
        Email = "msgraph-smoke@nexomail.local",
        PlanCode = CommercialPlanCatalog.Freemium,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
    });
    database.CommercialPlans.Add(new CommercialPlanEntity
    {
        Code = CommercialPlanCatalog.Freemium,
        Name = "Freemium",
        Price = "$0",
        Cadence = "Test",
        MaxAccounts = 1,
        Description = "Plan de prueba",
        FeaturesJson = "[]",
        EntitlementsJson = "[]",
        IsActive = true,
        SortOrder = 0,
        UpdatedAt = DateTimeOffset.UtcNow
    });
    database.MailAccounts.Add(new MailAccountEntity
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Provider = MailProviderType.Gmail,
        EmailAddress = "existing@nexomail.local",
        DisplayName = "Existing Gmail",
        Color = "#c6524b",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
    });
    await database.SaveChangesAsync();

    var policy = new MailAccountConnectionPolicy(database, new TestUserContext(userId));
    var blocked = false;
    try
    {
        await policy.EnsureCanConnectAnotherAccountAsync(CancellationToken.None);
    }
    catch (InvalidOperationException)
    {
        blocked = true;
    }

    Ensure(blocked, "El límite comercial debe bloquear una segunda cuenta.");
    Console.WriteLine("Microsoft Graph smoke: PASS");
}
finally
{
    if (File.Exists(databasePath)) File.Delete(databasePath);
}

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class TestUserContext(Guid userId) : IUserContext
{
    public bool IsAuthenticated => true;
    public Guid UserId => userId;
    public string Email => "msgraph-smoke@nexomail.local";
    public string DisplayName => "Microsoft Graph Smoke";
}
