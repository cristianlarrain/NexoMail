using NexoMail.Domain;

namespace NexoMail.Application;

public interface IMailProvider
{
    MailProviderType ProviderType { get; }
    Task<PagedResult<MailSummary>> GetMessagesAsync(MailQuery query, CancellationToken cancellationToken);
    Task<MailMessage?> GetMessageAsync(Guid accountId, string messageId, CancellationToken cancellationToken);
    Task<MailAttachmentContent?> GetAttachmentAsync(Guid accountId, string messageId, string attachmentId, CancellationToken cancellationToken);
    Task SendAsync(ComposeMessage message, CancellationToken cancellationToken);
    Task ReplyAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken);
    Task ReplyAllAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken);
    Task ForwardAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken);
    Task MarkReadAsync(Guid accountId, string messageId, bool read, CancellationToken cancellationToken);
    Task MoveToTrashAsync(Guid accountId, string messageId, CancellationToken cancellationToken);
    Task MoveToFolderAsync(Guid accountId, string messageId, string folderId, CancellationToken cancellationToken);
    Task EmptyFolderAsync(Guid accountId, string folderId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MailFolder>> GetFoldersAsync(Guid accountId, CancellationToken cancellationToken);
}

public interface IMailGateway
{
    Task<IReadOnlyCollection<MailAccount>> GetAccountsAsync(CancellationToken cancellationToken);
    Task<MailAccount?> UpdateAccountAsync(Guid accountId, MailAccountSettings settings, CancellationToken cancellationToken);
    Task<PagedResult<MailSummary>> GetMessagesAsync(MailQuery query, CancellationToken cancellationToken);
    Task<MailMessage?> GetMessageAsync(Guid accountId, string messageId, CancellationToken cancellationToken);
    Task<MailAttachmentContent?> GetAttachmentAsync(Guid accountId, string messageId, string attachmentId, CancellationToken cancellationToken);
    Task SendAsync(ComposeMessage message, CancellationToken cancellationToken);
    Task ReplyAsync(Guid accountId, string messageId, ComposeMessage message, bool replyAll, CancellationToken cancellationToken);
    Task ForwardAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken);
    Task MarkReadAsync(Guid accountId, string messageId, bool read, CancellationToken cancellationToken);
    Task MoveToTrashAsync(Guid accountId, string messageId, CancellationToken cancellationToken);
    Task MoveToFolderAsync(Guid accountId, string messageId, string folderId, CancellationToken cancellationToken);
    Task EmptyFolderAsync(Guid? accountId, string folderId, CancellationToken cancellationToken);
}
