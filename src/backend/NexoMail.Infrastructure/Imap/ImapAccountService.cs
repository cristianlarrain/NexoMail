using System.Net;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;

namespace NexoMail.Infrastructure.Imap;

public sealed class ImapAccountService(
    NexoMailDbContext database,
    ITokenProtector tokenProtector,
    IUserContext userContext)
{
    public async Task<MailAccount> ConnectAsync(ImapConnectionRequest request, CancellationToken ct)
    {
        var normalized = request.NormalizeAndValidate();
        await EnsurePublicHostAsync(normalized.ImapHost, ct);
        await EnsurePublicHostAsync(normalized.SmtpHost, ct);
        await ValidateConnectionsAsync(normalized, ct);
        await EnsureCanConnectAnotherAccountAsync(normalized.EmailAddress, ct);
        await ImapSchemaBootstrap.EnsureAsync(database, ct);

        var userId = userContext.UserId;
        var conflicting = await database.MailAccounts.SingleOrDefaultAsync(
            x => x.UserId == userId && x.EmailAddress == normalized.EmailAddress && x.Provider != MailProviderType.Imap, ct);
        if (conflicting is not null)
            throw new InvalidOperationException("Esta dirección ya está conectada con otro tipo de proveedor en NexoMail.");

        var account = await database.MailAccounts.SingleOrDefaultAsync(
            x => x.UserId == userId && x.EmailAddress == normalized.EmailAddress && x.Provider == MailProviderType.Imap, ct);
        if (account is null)
        {
            account = new MailAccountEntity
            {
                Id = Guid.NewGuid(), UserId = userId, Provider = MailProviderType.Imap,
                EmailAddress = normalized.EmailAddress,
                DisplayName = normalized.DisplayName,
                Color = "#496b7a", IsActive = true, CreatedAt = DateTimeOffset.UtcNow
            };
            database.MailAccounts.Add(account);
        }
        else
        {
            account.IsActive = true;
            account.DisplayName = normalized.DisplayName;
        }

        var credential = await database.ImapCredentials.SingleOrDefaultAsync(x => x.MailAccountId == account.Id, ct);
        if (credential is null)
        {
            credential = new ImapCredentialEntity { Id = Guid.NewGuid(), MailAccountId = account.Id };
            database.ImapCredentials.Add(credential);
        }
        credential.Username = normalized.Username;
        credential.EncryptedPassword = tokenProtector.Protect(normalized.Password);
        credential.ImapHost = normalized.ImapHost;
        credential.ImapPort = normalized.ImapPort;
        credential.ImapSecurity = normalized.ImapSecurity;
        credential.SmtpHost = normalized.SmtpHost;
        credential.SmtpPort = normalized.SmtpPort;
        credential.SmtpSecurity = normalized.SmtpSecurity;
        credential.UpdatedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(ct);

        return new MailAccount(account.Id, account.Provider, account.EmailAddress, account.DisplayName, account.Color, account.IsActive);
    }

    private async Task EnsureCanConnectAnotherAccountAsync(string emailAddress, CancellationToken ct)
    {
        var userId = userContext.UserId;
        var alreadyExists = await database.MailAccounts.AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.EmailAddress == emailAddress, ct);
        if (alreadyExists) return;

        var access = await CommercialAccessStore.GetAsync(database, userId, ct)
            ?? throw new InvalidOperationException("No fue posible determinar el plan de la cuenta.");
        if (!access.EffectivePlan.MaxAccounts.HasValue) return;
        var connected = await database.MailAccounts.AsNoTracking().CountAsync(x => x.UserId == userId && x.IsActive, ct);
        if (connected >= access.EffectivePlan.MaxAccounts.Value)
            throw new InvalidOperationException($"Su plan efectivo {access.EffectivePlan.Name} permite hasta {access.EffectivePlan.MaxAccounts.Value} cuentas de correo.");
    }

    private static async Task ValidateConnectionsAsync(ImapConnectionRequest request, CancellationToken ct)
    {
        using (var imap = new ImapClient())
        {
            imap.Timeout = 10_000;
            await imap.ConnectAsync(request.ImapHost, request.ImapPort, SocketOptions(request.ImapSecurity), ct);
            imap.AuthenticationMechanisms.Remove("XOAUTH2");
            await imap.AuthenticateAsync(request.Username, request.Password, ct);
            await imap.DisconnectAsync(true, ct);
        }

        using var smtp = new SmtpClient();
        smtp.Timeout = 10_000;
        await smtp.ConnectAsync(request.SmtpHost, request.SmtpPort, SocketOptions(request.SmtpSecurity), ct);
        smtp.AuthenticationMechanisms.Remove("XOAUTH2");
        await smtp.AuthenticateAsync(request.Username, request.Password, ct);
        await smtp.DisconnectAsync(true, ct);
    }

    internal static SecureSocketOptions SocketOptions(string security) => security switch
    {
        "ssl" => SecureSocketOptions.SslOnConnect,
        "starttls" => SecureSocketOptions.StartTls,
        _ => throw new InvalidOperationException("Modo de seguridad no admitido.")
    };

    private static async Task EnsurePublicHostAsync(string host, CancellationToken ct)
    {
        IPAddress[] addresses;
        try { addresses = await Dns.GetHostAddressesAsync(host, ct); }
        catch { throw new InvalidOperationException($"No fue posible resolver el servidor {host}."); }
        if (addresses.Length == 0 || addresses.Any(IsPrivateOrLocal))
            throw new InvalidOperationException("Por seguridad, el servidor de correo debe resolver únicamente a direcciones públicas.");
    }

    private static bool IsPrivateOrLocal(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None);
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || bytes[0] == 127
            || bytes[0] == 0
            || bytes[0] == 169 && bytes[1] == 254
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
            || bytes[0] == 192 && bytes[1] == 168
            || bytes[0] == 100 && bytes[1] is >= 64 and <= 127;
    }
}
