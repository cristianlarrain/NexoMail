using System.Net;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;

namespace NexoMail.Infrastructure.Imap;

public sealed class ImapAccountService(
    NexoMailDbContext database,
    ITokenProtector tokenProtector,
    IUserContext userContext,
    ILogger<ImapAccountService> logger)
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
            var usedColors = await database.MailAccounts.AsNoTracking()
                .Where(x => x.UserId == userId && x.IsActive)
                .Select(x => x.Color)
                .ToArrayAsync(ct);
            account = new MailAccountEntity
            {
                Id = Guid.NewGuid(), UserId = userId, Provider = MailProviderType.Imap,
                EmailAddress = normalized.EmailAddress,
                DisplayName = normalized.DisplayName,
                Color = AccountColorSelector.Select(usedColors), IsActive = true, CreatedAt = DateTimeOffset.UtcNow
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

    private async Task ValidateConnectionsAsync(ImapConnectionRequest request, CancellationToken ct)
    {
        using (var imap = new ImapClient { Timeout = 10_000 })
        {
            try
            {
                await imap.ConnectAsync(request.ImapHost, request.ImapPort, SocketOptions(request.ImapSecurity), ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw ConnectionFailure("IMAP", request.ImapHost, request.ImapPort, request.ImapSecurity, exception);
            }
            imap.AuthenticationMechanisms.Remove("XOAUTH2");
            try { await imap.AuthenticateAsync(request.Username, request.Password, ct); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw AuthenticationFailure("IMAP", request.ImapHost, exception);
            }
            await imap.DisconnectAsync(true, ct);
        }

        using var smtp = new SmtpClient { Timeout = 10_000 };
        try
        {
            await smtp.ConnectAsync(request.SmtpHost, request.SmtpPort, SocketOptions(request.SmtpSecurity), ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw ConnectionFailure("SMTP", request.SmtpHost, request.SmtpPort, request.SmtpSecurity, exception);
        }
        smtp.AuthenticationMechanisms.Remove("XOAUTH2");
        try { await smtp.AuthenticateAsync(request.Username, request.Password, ct); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw AuthenticationFailure("SMTP", request.SmtpHost, exception);
        }
        await smtp.DisconnectAsync(true, ct);
    }

    private InvalidOperationException ConnectionFailure(string protocol, string host, int port, string security, Exception exception)
    {
        logger.LogWarning(exception, "IMAP setup failed during {Protocol} connection to {Host}:{Port} using {Security}.", protocol, host, port, security);
        return new InvalidOperationException($"{protocol}: no fue posible establecer la conexión con {host}:{port}. Revise el puerto y el modo TLS ({security}).", exception);
    }

    private InvalidOperationException AuthenticationFailure(string protocol, string host, Exception exception)
    {
        logger.LogWarning(exception, "IMAP setup failed during {Protocol} authentication against {Host}.", protocol, host);
        return new InvalidOperationException($"{protocol}: el servidor {host} rechazó la autenticación. Revise el usuario y la contraseña del buzón.", exception);
    }

    internal static SecureSocketOptions SocketOptions(string security) => security switch
    {
        "ssl" => SecureSocketOptions.SslOnConnect,
        "starttls" => SecureSocketOptions.StartTls,
        _ => throw new InvalidOperationException("Modo de seguridad no admitido.")
    };

    private async Task EnsurePublicHostAsync(string host, CancellationToken ct)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "IMAP setup failed during DNS resolution for {Host}.", host);
            throw new InvalidOperationException($"DNS: no fue posible resolver el servidor {host}.", exception);
        }

        logger.LogInformation("IMAP setup DNS resolved {Host} to {Addresses}.", host, string.Join(", ", addresses.Select(x => x.ToString())));
        if (addresses.Length == 0)
            throw new InvalidOperationException($"DNS: el servidor {host} no entregó ninguna dirección IP.");

        var blocked = addresses.Where(address => !MailHostAddressPolicy.IsPublic(address)).ToArray();
        if (blocked.Length > 0)
        {
            logger.LogWarning("IMAP setup blocked non-public addresses for {Host}: {Addresses}.", host, string.Join(", ", blocked.Select(x => x.ToString())));
            throw new InvalidOperationException($"DNS: el servidor {host} resolvió a una dirección no pública ({string.Join(", ", blocked.Select(x => x.ToString()))}).");
        }
    }
}
