using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure;

public sealed class MailGateway(IEnumerable<IMailProvider> providers, NexoMailDbContext database, IUserContext userContext) : IMailGateway
{
    private readonly IReadOnlyDictionary<MailProviderType, IMailProvider> _providers = providers.ToDictionary(x => x.ProviderType);

    public async Task<IReadOnlyCollection<MailAccount>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var accounts = await database.MailAccounts
            .Where(x => x.UserId == userId && x.IsActive)
            .Select(x => new MailAccount(x.Id, x.Provider, x.EmailAddress, x.DisplayName, x.Color, x.IsActive))
            .ToArrayAsync(cancellationToken);
        return accounts.OrderBy(x => x.EmailAddress, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<MailAccount?> UpdateAccountAsync(Guid accountId, MailAccountSettings settings, CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var account = await database.MailAccounts.SingleOrDefaultAsync(x => x.Id == accountId && x.UserId == userId, cancellationToken);
        if (account is null) return null;
        account.DisplayName = settings.DisplayName;
        account.Color = settings.Color;
        await database.SaveChangesAsync(cancellationToken);
        return new MailAccount(account.Id, account.Provider, account.EmailAddress, account.DisplayName, account.Color, account.IsActive);
    }

    public async Task<PagedResult<MailSummary>> GetMessagesAsync(MailQuery query, CancellationToken cancellationToken)
    {
        if (query.AccountId.HasValue)
            return await ProviderFor(await AccountAsync(query.AccountId.Value, cancellationToken)).GetMessagesAsync(query, cancellationToken);

        var accounts = await GetAccountsAsync(cancellationToken);
        var cursors = string.IsNullOrWhiteSpace(query.Cursor) ? null : DecodeCursor(query.Cursor);
        var activeAccounts = cursors is null
            ? accounts
            : accounts.Where(account => cursors.TryGetValue(account.Id, out var cursor) && cursor is not null).ToArray();

        var results = await Task.WhenAll(activeAccounts.Select(async account => new
        {
            Account = account,
            Result = await TryGetMessagesAsync(account, query with
            {
                AccountId = account.Id,
                Cursor = cursors is null ? null : cursors.GetValueOrDefault(account.Id)
            }, cancellationToken)
        }));

        var items = results
            .Where(x => x.Result is not null)
            .SelectMany(x => x.Result!.Items)
            .OrderByDescending(x => x.ReceivedAt)
            .ToArray();

        var next = accounts.ToDictionary(account => account.Id, _ => (string?)null);
        foreach (var result in results)
            next[result.Account.Id] = result.Result?.NextCursor;

        return new PagedResult<MailSummary>(items, next.Values.Any(value => value is not null) ? EncodeCursor(next) : null);
    }

    public async Task<MailMessage?> GetMessageAsync(Guid accountId, string messageId, CancellationToken cancellationToken) => await ProviderFor(await AccountAsync(accountId, cancellationToken)).GetMessageAsync(accountId, messageId, cancellationToken);
    public async Task<MailAttachmentContent?> GetAttachmentAsync(Guid accountId, string messageId, string attachmentId, CancellationToken cancellationToken) => await ProviderFor(await AccountAsync(accountId, cancellationToken)).GetAttachmentAsync(accountId, messageId, attachmentId, cancellationToken);
    public async Task SendAsync(ComposeMessage message, CancellationToken cancellationToken) => await ProviderFor(await AccountAsync(message.FromAccountId, cancellationToken)).SendAsync(message, cancellationToken);
    public async Task ReplyAsync(Guid accountId, string messageId, ComposeMessage message, bool replyAll, CancellationToken cancellationToken) { var provider = ProviderFor(await AccountAsync(accountId, cancellationToken)); if (replyAll) await provider.ReplyAllAsync(accountId, messageId, message, cancellationToken); else await provider.ReplyAsync(accountId, messageId, message, cancellationToken); }
    public async Task ForwardAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken) => await ProviderFor(await AccountAsync(accountId, cancellationToken)).ForwardAsync(accountId, messageId, message, cancellationToken);
    public async Task MarkReadAsync(Guid accountId, string messageId, bool read, CancellationToken cancellationToken) => await ProviderFor(await AccountAsync(accountId, cancellationToken)).MarkReadAsync(accountId, messageId, read, cancellationToken);
    public async Task MoveToTrashAsync(Guid accountId, string messageId, CancellationToken cancellationToken) => await ProviderFor(await AccountAsync(accountId, cancellationToken)).MoveToTrashAsync(accountId, messageId, cancellationToken);
    public async Task MoveToFolderAsync(Guid accountId, string messageId, string folderId, CancellationToken cancellationToken) => await ProviderFor(await AccountAsync(accountId, cancellationToken)).MoveToFolderAsync(accountId, messageId, folderId, cancellationToken);

    public async Task EmptyFolderAsync(Guid? accountId, string folderId, CancellationToken cancellationToken)
    {
        if (accountId.HasValue)
        {
            var account = await AccountAsync(accountId.Value, cancellationToken);
            await ProviderFor(account).EmptyFolderAsync(account.Id, folderId, cancellationToken);
            return;
        }
        var accounts = await GetAccountsAsync(cancellationToken);
        await Task.WhenAll(accounts.Select(account => ProviderFor(account).EmptyFolderAsync(account.Id, folderId, cancellationToken)));
    }

    private async Task<MailAccount> AccountAsync(Guid id, CancellationToken ct)
    {
        var userId = userContext.UserId;
        return await database.MailAccounts
            .Where(x => x.Id == id && x.UserId == userId && x.IsActive)
            .Select(x => new MailAccount(x.Id, x.Provider, x.EmailAddress, x.DisplayName, x.Color, x.IsActive))
            .SingleAsync(ct);
    }

    private IMailProvider ProviderFor(MailAccount account) => _providers.TryGetValue(account.Provider, out var provider) ? provider : throw new NotSupportedException($"El proveedor {account.Provider} aún no está disponible.");

    private async Task<PagedResult<MailSummary>?> TryGetMessagesAsync(MailAccount account, MailQuery query, CancellationToken ct)
    {
        try { return await ProviderFor(account).GetMessagesAsync(query with { AccountId = account.Id }, ct); }
        catch (HttpRequestException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static string EncodeCursor(Dictionary<Guid, string?> cursors)
    {
        var json = JsonSerializer.Serialize(cursors);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static Dictionary<Guid, string?> DecodeCursor(string cursor)
    {
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/') + new string('=', (4 - cursor.Length % 4) % 4);
            return JsonSerializer.Deserialize<Dictionary<Guid, string?>>(Encoding.UTF8.GetString(Convert.FromBase64String(padded))) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
