using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure;

/// <summary>
/// Defense-in-depth wrapper that prevents a mail provider from operating on
/// accounts that do not belong to the currently authenticated NexoMail user.
/// </summary>
public sealed class UserScopedMailProvider(
    IMailProvider inner,
    NexoMailDbContext database,
    IUserContext userContext) : IMailProvider
{
    public MailProviderType ProviderType => inner.ProviderType;

    public async Task<PagedResult<MailSummary>> GetMessagesAsync(MailQuery query, CancellationToken cancellationToken)
    {
        if (query.AccountId.HasValue)
            await EnsureAccountAccessAsync(query.AccountId.Value, cancellationToken);
        return await inner.GetMessagesAsync(query, cancellationToken);
    }

    internal Task<PagedResult<MailSummary>> GetMessagesForAuthorizedAccountAsync(MailQuery query, CancellationToken cancellationToken) =>
        inner.GetMessagesAsync(query, cancellationToken);

    public async Task<MailMessage?> GetMessageAsync(Guid accountId, string messageId, CancellationToken cancellationToken)
    {
        await EnsureAccountAccessAsync(accountId, cancellationToken);
        return await inner.GetMessageAsync(accountId, messageId, cancellationToken);
    }

    public async Task<MailAttachmentContent?> GetAttachmentAsync(Guid accountId, string messageId, string attachmentId, CancellationToken cancellationToken)
    {
        await EnsureAccountAccessAsync(accountId, cancellationToken);
        return await inner.GetAttachmentAsync(accountId, messageId, attachmentId, cancellationToken);
    }

    public async Task SendAsync(ComposeMessage message, CancellationToken cancellationToken)
    {
        await EnsureAccountAccessAsync(message.FromAccountId, cancellationToken);
        await inner.SendAsync(message, cancellationToken);
    }

    public async Task ReplyAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken)
    {
        await EnsureAccountAccessAsync(accountId, cancellationToken);
        await inner.ReplyAsync(accountId, messageId, message, cancellationToken);
    }

    public async Task ReplyAllAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken)
    {
        await EnsureAccountAccessAsync(accountId, cancellationToken);
        await inner.ReplyAllAsync(accountId, messageId, message, cancellationToken);
    }

    public async Task ForwardAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken)
    {
        await EnsureAccountAccessAsync(accountId, cancellationToken);
        await inner.ForwardAsync(accountId, messageId, message, cancellationToken);
    }

    public async Task MarkReadAsync(Guid accountId, string messageId, bool read, CancellationToken cancellationToken)
    {
        await EnsureAccountAccessAsync(accountId, cancellationToken);
        await inner.MarkReadAsync(accountId, messageId, read, cancellationToken);
    }

    public async Task MoveToTrashAsync(Guid accountId, string messageId, CancellationToken cancellationToken)
    {
        await EnsureAccountAccessAsync(accountId, cancellationToken);
        await inner.MoveToTrashAsync(accountId, messageId, cancellationToken);
    }

    public async Task MoveToFolderAsync(Guid accountId, string messageId, string folderId, CancellationToken cancellationToken)
    {
        await EnsureAccountAccessAsync(accountId, cancellationToken);
        await inner.MoveToFolderAsync(accountId, messageId, folderId, cancellationToken);
    }

    public async Task EmptyFolderAsync(Guid accountId, string folderId, CancellationToken cancellationToken)
    {
        await EnsureAccountAccessAsync(accountId, cancellationToken);
        await inner.EmptyFolderAsync(accountId, folderId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<MailFolder>> GetFoldersAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await EnsureAccountAccessAsync(accountId, cancellationToken);
        return await inner.GetFoldersAsync(accountId, cancellationToken);
    }

    private async Task EnsureAccountAccessAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var hasAccess = await database.MailAccounts
            .AsNoTracking()
            .AnyAsync(
                account => account.Id == accountId
                    && account.UserId == userId
                    && account.IsActive
                    && account.Provider == ProviderType,
                cancellationToken);

        if (!hasAccess)
            throw new KeyNotFoundException("La cuenta de correo no está disponible.");
    }
}
