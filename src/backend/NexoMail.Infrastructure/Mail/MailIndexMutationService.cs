using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Mail;

/// <summary>
/// Keeps normalized indexed metadata convergent after a provider mutation succeeds.
/// This service never calls a mail provider and never fabricates provider message identifiers.
/// </summary>
public sealed class MailIndexMutationService(
    NexoMailDbContext database,
    IUserContext userContext)
{
    public async Task MarkReadAsync(
        Guid accountId,
        string providerMessageId,
        bool read,
        CancellationToken cancellationToken)
    {
        var row = await FindOwnedMessageAsync(accountId, providerMessageId, cancellationToken);
        if (row is null)
        {
            await MarkAccountForImmediateSyncAsync(accountId, cancellationToken);
            return;
        }

        row.IsUnread = !read;
        row.IndexedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task MoveAsync(
        Guid accountId,
        string providerMessageId,
        string folderId,
        CancellationToken cancellationToken)
    {
        var normalizedFolder = folderId.Trim().ToLowerInvariant();
        if (normalizedFolder is not ("archive" or "spam" or "trash" or "inbox"))
            throw new InvalidOperationException("La carpeta solicitada no admite una transición local del índice.");

        var row = await FindOwnedMessageAsync(accountId, providerMessageId, cancellationToken);
        if (row is null)
        {
            await MarkAccountForImmediateSyncAsync(accountId, cancellationToken);
            return;
        }

        switch (normalizedFolder)
        {
            case "archive":
                row.IsInbox = false;
                break;
            case "spam":
                row.IsSpam = true;
                row.IsInbox = false;
                row.IsTrash = false;
                break;
            case "trash":
                row.IsTrash = true;
                row.IsInbox = false;
                row.IsSpam = false;
                break;
            case "inbox":
                row.IsInbox = true;
                row.IsTrash = false;
                row.IsSpam = false;
                break;
        }

        row.IndexedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAccountForImmediateSyncAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var ownsActiveAccount = await database.MailAccounts
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == accountId
                     && x.UserId == userContext.UserId
                     && x.IsActive,
                cancellationToken);
        if (!ownsActiveAccount) return;

        var state = await database.MailIndexStates
            .SingleOrDefaultAsync(
                x => x.AccountId == accountId && x.UserId == userContext.UserId,
                cancellationToken);
        if (state is null) return;

        state.LastIndexedAt = DateTimeOffset.UnixEpoch;
        state.LastSyncErrorCode = null;
        await database.SaveChangesAsync(cancellationToken);
    }

    private Task<MailMessageIndexEntity?> FindOwnedMessageAsync(
        Guid accountId,
        string providerMessageId,
        CancellationToken cancellationToken)
    {
        var normalizedMessageId = providerMessageId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessageId))
            return Task.FromResult<MailMessageIndexEntity?>(null);

        return database.MailMessageIndex.SingleOrDefaultAsync(
            x => x.UserId == userContext.UserId
                 && x.AccountId == accountId
                 && x.ProviderMessageId == normalizedMessageId,
            cancellationToken);
    }
}
