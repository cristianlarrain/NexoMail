using Microsoft.EntityFrameworkCore;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

internal static class MailProviderPersistenceSmoke
{
    public static async Task RunAsync(NexoMailDbContext database, UserEntity user, CancellationToken ct)
    {
        var account = new MailAccountEntity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Provider = MailProviderType.Imap,
            EmailAddress = $"imap-smoke-{Guid.NewGuid():N}@nexomail.test",
            DisplayName = "IMAP Beta",
            Color = "#496b7a",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };
        database.MailAccounts.Add(account);
        database.ImapCredentials.Add(new ImapCredentialEntity
        {
            Id = Guid.NewGuid(),
            MailAccountId = account.Id,
            Username = account.EmailAddress,
            EncryptedPassword = "protected-smoke-value",
            ImapHost = "imap.nexomail.test",
            ImapPort = 993,
            ImapSecurity = "ssl",
            SmtpHost = "smtp.nexomail.test",
            SmtpPort = 587,
            SmtpSecurity = "starttls",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await database.SaveChangesAsync(ct);

        var stored = await database.ImapCredentials.AsNoTracking()
            .SingleOrDefaultAsync(x => x.MailAccountId == account.Id, ct);
        if (stored is null || stored.EncryptedPassword != "protected-smoke-value")
            throw new InvalidOperationException("Las credenciales IMAP/SMTP deben persistirse asociadas 1:1 a la cuenta.");
    }
}
