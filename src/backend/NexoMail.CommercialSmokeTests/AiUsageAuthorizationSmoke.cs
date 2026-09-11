using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using NexoMail.Api;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

internal static class AiUsageAuthorizationSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync(CancellationToken.None).GetAwaiter().GetResult();

    private static async Task RunAsync(CancellationToken ct)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"nexomail-ai-owner-auth-{Guid.NewGuid():N}.db");
        try
        {
            await using var database = new NexoMailDbContext(
                new DbContextOptionsBuilder<NexoMailDbContext>().UseSqlite($"Data Source={dbPath};Pooling=False").Options);
            await database.Database.EnsureCreatedAsync(ct);

            var owner = User("owner@nexomail.test", isOwner: true, isAdministrator: true);
            var administrator = User("admin@nexomail.test", isOwner: false, isAdministrator: true);
            var regular = User("user@nexomail.test", isOwner: false, isAdministrator: false);
            database.Users.AddRange(owner, administrator, regular);
            await database.SaveChangesAsync(ct);

            Require(await AiUsageEndpoints.IsOwnerAsync(database, new FakeUserContext(owner), ct), "Owner must be allowed.");
            Require(!await AiUsageEndpoints.IsOwnerAsync(database, new FakeUserContext(administrator), ct), "Administrator without Owner authority must be forbidden.");
            Require(!await AiUsageEndpoints.IsOwnerAsync(database, new FakeUserContext(regular), ct), "Normal user must be forbidden.");
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

    private static UserEntity User(string email, bool isOwner, bool isAdministrator) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = email.Split('@')[0],
        Email = email,
        PlanCode = "freemium",
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true,
        IsEmailVerified = true,
        IsOwner = isOwner,
        IsAdministrator = isAdministrator
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeUserContext(UserEntity user) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid UserId => user.Id;
        public string Email => user.Email;
        public string DisplayName => user.DisplayName;
    }
}
