using System.Net;
using System.Text;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;
using DomainMailFolder = NexoMail.Domain.MailFolder;

namespace NexoMail.Infrastructure.Imap;

public sealed class ImapMailProvider(NexoMailDbContext database, ITokenProtector tokenProtector) : IMailProvider
{
    public MailProviderType ProviderType => MailProviderType.Imap;

    public async Task<PagedResult<MailSummary>> GetMessagesAsync(MailQuery query, CancellationToken ct)
    {
        if (!query.AccountId.HasValue) return new PagedResult<MailSummary>([]);
        var snapshot = await SnapshotAsync(query.AccountId.Value, ct);
        using var client = await ConnectImapAsync(snapshot, ct);
        var folder = await ResolveFolderAsync(client, query.FolderId, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);
        var search = string.IsNullOrWhiteSpace(query.Search) ? SearchQuery.All : SearchQuery.MessageContains(query.Search.Trim());
        var uids = await folder.SearchAsync(search, ct);
        var offset = int.TryParse(query.Cursor, out var parsed) ? Math.Max(0, parsed) : 0;
        var page = uids.OrderByDescending(uid => uid.Id).Skip(offset).Take(Math.Clamp(query.Take, 1, 50)).ToList();
        if (page.Count == 0) return new PagedResult<MailSummary>([]);
        var summaries = await folder.FetchAsync(page,
            MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
            MessageSummaryItems.InternalDate | MessageSummaryItems.BodyStructure | MessageSummaryItems.PreviewText, ct);
        var items = summaries.Select(summary => Summary(summary, query.AccountId.Value, folder.FullName, query.FolderId))
            .OrderByDescending(item => item.ReceivedAt).ToArray();
        var next = offset + page.Count < uids.Count ? (offset + page.Count).ToString() : null;
        return new PagedResult<MailSummary>(items, next);
    }

    public async Task<MailMessage?> GetMessageAsync(Guid accountId, string messageId, CancellationToken ct)
    {
        var key = DecodeMessageKey(messageId);
        var snapshot = await SnapshotAsync(accountId, ct);
        using var client = await ConnectImapAsync(snapshot, ct);
        var folder = await client.GetFolderAsync(key.Folder, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);
        var uid = new UniqueId(key.Uid);
        var found = await folder.SearchAsync(SearchQuery.Uids([uid]), ct);
        if (found.Count == 0) return null;
        var message = await folder.GetMessageAsync(uid, ct);
        return ToMailMessage(message, accountId, messageId, FolderAlias(client, folder));
    }

    public async Task<IReadOnlyCollection<MailThreadMessage>> GetThreadAsync(Guid accountId, string messageId, CancellationToken ct)
    {
        var message = await GetMessageAsync(accountId, messageId, ct);
        return message is null ? [] : [new MailThreadMessage(message.ProviderMessageId, message.From, message.HtmlBody, message.ReceivedAt, true)];
    }

    public async Task<MailAttachmentContent?> GetAttachmentAsync(Guid accountId, string messageId, string attachmentId, CancellationToken ct)
    {
        var key = DecodeMessageKey(messageId);
        var snapshot = await SnapshotAsync(accountId, ct);
        using var client = await ConnectImapAsync(snapshot, ct);
        var folder = await client.GetFolderAsync(key.Folder, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);
        var message = await folder.GetMessageAsync(new UniqueId(key.Uid), ct);
        if (!int.TryParse(attachmentId, out var index) || index < 0) return null;
        var attachment = message.Attachments.ElementAtOrDefault(index);
        if (attachment is null) return null;
        using var stream = new MemoryStream();
        if (attachment is MimePart part)
        {
            if (part.Content is null) return null;
            await part.Content.DecodeToAsync(stream, ct);
            return new MailAttachmentContent(stream.ToArray(), part.ContentType.MimeType,
                part.FileName ?? part.ContentType.Name ?? $"adjunto-{index + 1}");
        }
        if (attachment is MessagePart messagePart)
        {
            if (messagePart.Message is null) return null;
            await messagePart.Message.WriteToAsync(stream, ct);
            return new MailAttachmentContent(stream.ToArray(), "message/rfc822",
                messagePart.ContentDisposition?.FileName ?? messagePart.ContentType.Name ?? $"mensaje-{index + 1}.eml");
        }
        return null;
    }

    public async Task SendAsync(ComposeMessage message, CancellationToken ct)
    {
        var snapshot = await SnapshotAsync(message.FromAccountId, ct);
        await SendMimeAsync(snapshot, BuildOutgoing(snapshot, message), ct);
    }

    public Task ReplyAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken ct) => SendReplyAsync(accountId, messageId, message, ct);
    public Task ReplyAllAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken ct) => SendReplyAsync(accountId, messageId, message, ct);
    public Task ForwardAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken ct) => SendAsync(message with { FromAccountId = accountId }, ct);

    public async Task MarkReadAsync(Guid accountId, string messageId, bool read, CancellationToken ct)
    {
        var key = DecodeMessageKey(messageId);
        var snapshot = await SnapshotAsync(accountId, ct);
        using var client = await ConnectImapAsync(snapshot, ct);
        var folder = await client.GetFolderAsync(key.Folder, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);
        var uid = new UniqueId(key.Uid);
        if (read) await folder.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
        else await folder.RemoveFlagsAsync(uid, MessageFlags.Seen, true, ct);
    }

    public Task MoveToTrashAsync(Guid accountId, string messageId, CancellationToken ct) => MoveToFolderAsync(accountId, messageId, "trash", ct);

    public async Task MoveToFolderAsync(Guid accountId, string messageId, string folderId, CancellationToken ct)
    {
        var key = DecodeMessageKey(messageId);
        var snapshot = await SnapshotAsync(accountId, ct);
        using var client = await ConnectImapAsync(snapshot, ct);
        var source = await client.GetFolderAsync(key.Folder, ct);
        var destination = await ResolveFolderAsync(client, folderId, ct);
        await source.OpenAsync(FolderAccess.ReadWrite, ct);
        await source.MoveToAsync(new UniqueId(key.Uid), destination, ct);
    }

    public async Task EmptyFolderAsync(Guid accountId, string folderId, CancellationToken ct)
    {
        if (!string.Equals(folderId, "trash", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Durante la Beta sólo se puede vaciar la Papelera.");
        var snapshot = await SnapshotAsync(accountId, ct);
        using var client = await ConnectImapAsync(snapshot, ct);
        var folder = await ResolveFolderAsync(client, folderId, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);
        var uids = await folder.SearchAsync(SearchQuery.All, ct);
        if (uids.Count == 0) return;
        await folder.AddFlagsAsync(uids, MessageFlags.Deleted, true, ct);
        await folder.ExpungeAsync(ct);
    }

    public async Task<IReadOnlyCollection<DomainMailFolder>> GetFoldersAsync(Guid accountId, CancellationToken ct)
    {
        var snapshot = await SnapshotAsync(accountId, ct);
        using var client = await ConnectImapAsync(snapshot, ct);
        var folders = new List<DomainMailFolder>
        {
            new("inbox", "Bandeja de entrada", 0), new("archive", "Archivados", 0),
            new("sent", "Enviados", 0), new("drafts", "Borradores", 0),
            new("spam", "Spam", 0), new("trash", "Papelera", 0)
        };
        if (client.PersonalNamespaces.Count == 0) return folders;
        var all = await client.GetFoldersAsync(client.PersonalNamespaces[0], false, ct);
        var systemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { client.Inbox.FullName };
        foreach (var special in new[] { SpecialFolder.Archive, SpecialFolder.Sent, SpecialFolder.Drafts, SpecialFolder.Junk, SpecialFolder.Trash })
        {
            try { var folder = client.GetFolder(special); if (folder is not null) systemNames.Add(folder.FullName); } catch { }
        }
        foreach (var folder in all.Where(folder => !folder.Attributes.HasFlag(FolderAttributes.NonExistent) && !systemNames.Contains(folder.FullName)))
            folders.Add(new DomainMailFolder(EncodeCustomFolder(folder.FullName), folder.Name, 0));
        return folders;
    }

    private async Task SendReplyAsync(Guid accountId, string messageId, ComposeMessage compose, CancellationToken ct)
    {
        var key = DecodeMessageKey(messageId);
        var snapshot = await SnapshotAsync(accountId, ct);
        using var client = await ConnectImapAsync(snapshot, ct);
        var folder = await client.GetFolderAsync(key.Folder, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);
        var original = await folder.GetMessageAsync(new UniqueId(key.Uid), ct);
        var outgoing = BuildOutgoing(snapshot, compose with { FromAccountId = accountId });
        if (!string.IsNullOrWhiteSpace(original.MessageId)) outgoing.InReplyTo = original.MessageId;
        foreach (var reference in original.References) outgoing.References.Add(reference);
        if (!string.IsNullOrWhiteSpace(original.MessageId) && !outgoing.References.Contains(original.MessageId)) outgoing.References.Add(original.MessageId);
        await SendMimeAsync(snapshot, outgoing, ct);
    }

    private async Task<ImapSnapshot> SnapshotAsync(Guid accountId, CancellationToken ct)
    {
        var row = await database.MailAccounts.AsNoTracking()
            .Where(account => account.Id == accountId && account.Provider == MailProviderType.Imap && account.IsActive)
            .Join(database.ImapCredentials.AsNoTracking(), account => account.Id, credential => credential.MailAccountId,
                (account, credential) => new { account, credential }).SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No existe una configuración IMAP/SMTP válida para esta cuenta.");
        return new ImapSnapshot(row.account.EmailAddress, row.account.DisplayName, row.credential.Username,
            tokenProtector.Unprotect(row.credential.EncryptedPassword), row.credential.ImapHost, row.credential.ImapPort,
            row.credential.ImapSecurity, row.credential.SmtpHost, row.credential.SmtpPort, row.credential.SmtpSecurity);
    }

    private static async Task<ImapClient> ConnectImapAsync(ImapSnapshot snapshot, CancellationToken ct)
    {
        var client = new ImapClient { Timeout = 15_000 };
        try
        {
            await client.ConnectAsync(snapshot.ImapHost, snapshot.ImapPort, ImapAccountService.SocketOptions(snapshot.ImapSecurity), ct);
            client.AuthenticationMechanisms.Remove("XOAUTH2");
            await client.AuthenticateAsync(snapshot.Username, snapshot.Password, ct);
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    private static async Task SendMimeAsync(ImapSnapshot snapshot, MimeMessage message, CancellationToken ct)
    {
        using var client = new SmtpClient { Timeout = 15_000 };
        await client.ConnectAsync(snapshot.SmtpHost, snapshot.SmtpPort, ImapAccountService.SocketOptions(snapshot.SmtpSecurity), ct);
        client.AuthenticationMechanisms.Remove("XOAUTH2");
        await client.AuthenticateAsync(snapshot.Username, snapshot.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }

    private static async Task<IMailFolder> ResolveFolderAsync(ImapClient client, string folderId, CancellationToken ct)
    {
        if (folderId.StartsWith("custom:", StringComparison.OrdinalIgnoreCase)) return await client.GetFolderAsync(DecodeCustomFolder(folderId), ct);
        if (!string.IsNullOrWhiteSpace(folderId) && !IsCanonicalFolder(folderId)) return await client.GetFolderAsync(folderId, ct);
        if (string.Equals(folderId, "inbox", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(folderId)) return client.Inbox;
        var special = folderId.ToLowerInvariant() switch
        {
            "archive" => SpecialFolder.Archive, "sent" => SpecialFolder.Sent, "drafts" => SpecialFolder.Drafts,
            "spam" => SpecialFolder.Junk, "trash" => SpecialFolder.Trash,
            _ => throw new InvalidOperationException("NexoMail no reconoce la carpeta IMAP solicitada.")
        };
        return client.GetFolder(special) ?? throw new InvalidOperationException($"El servidor IMAP no publica una carpeta compatible con {folderId}.");
    }

    private static MailSummary Summary(IMessageSummary summary, Guid accountId, string fullName, string folderId)
    {
        var from = summary.Envelope?.From?.Mailboxes.FirstOrDefault();
        var date = summary.InternalDate ?? summary.Date;
        return new MailSummary(EncodeMessageKey(fullName, summary.UniqueId), accountId,
            from?.Name ?? from?.Address ?? string.Empty, from?.Address ?? string.Empty,
            summary.Envelope?.Subject ?? "(sin asunto)", summary.PreviewText ?? string.Empty,
            date == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : date,
            summary.Flags?.HasFlag(MessageFlags.Seen) == true, summary.Attachments.Any(), folderId);
    }

    private static MailMessage ToMailMessage(MimeMessage source, Guid accountId, string providerMessageId, string folderId)
    {
        var from = source.From.Mailboxes.FirstOrDefault();
        var html = string.IsNullOrWhiteSpace(source.HtmlBody) ? "<pre>" + WebUtility.HtmlEncode(source.TextBody ?? string.Empty) + "</pre>" : source.HtmlBody;
        var attachments = source.Attachments.Select((attachment, index) => new MailAttachment(index.ToString(), AttachmentName(attachment, index), attachment.ContentType.MimeType, 0)).ToArray();
        var received = source.Date == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : source.Date;
        return new MailMessage(providerMessageId, accountId,
            new MailAddress(from?.Name ?? from?.Address ?? string.Empty, from?.Address ?? string.Empty),
            source.To.Mailboxes.Select(ToAddress).ToArray(), source.Cc.Mailboxes.Select(ToAddress).ToArray(),
            source.Subject ?? "(sin asunto)", html, Preview(source), received, true, attachments, folderId, null,
            SafeUnsubscribe(source.Headers[HeaderId.ListUnsubscribe]));
    }

    private static MimeMessage BuildOutgoing(ImapSnapshot snapshot, ComposeMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(snapshot.DisplayName, snapshot.EmailAddress));
        foreach (var address in message.To) mime.To.Add(MailboxAddress.Parse(address));
        foreach (var address in message.Cc) mime.Cc.Add(MailboxAddress.Parse(address));
        foreach (var address in message.Bcc) mime.Bcc.Add(MailboxAddress.Parse(address));
        mime.Subject = message.Subject ?? string.Empty;
        var builder = new BodyBuilder { HtmlBody = message.HtmlBody ?? string.Empty };
        foreach (var attachment in message.Attachments ?? [])
        {
            ContentType type;
            try { type = ContentType.Parse(attachment.ContentType); } catch { type = new ContentType("application", "octet-stream"); }
            builder.Attachments.Add(attachment.Name, Convert.FromBase64String(attachment.Base64Content), type);
        }
        mime.Body = builder.ToMessageBody();
        return mime;
    }

    private static MailAddress ToAddress(MailboxAddress mailbox) => new(mailbox.Name ?? mailbox.Address, mailbox.Address);
    private static string Preview(MimeMessage message)
    {
        var text = message.TextBody;
        if (string.IsNullOrWhiteSpace(text)) text = System.Text.RegularExpressions.Regex.Replace(message.HtmlBody ?? string.Empty, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 280 ? text : text[..280];
    }
    private static string AttachmentName(MimeEntity entity, int index) => entity switch
    {
        MimePart part => part.FileName ?? part.ContentType.Name ?? $"adjunto-{index + 1}",
        MessagePart part => part.ContentDisposition?.FileName ?? part.ContentType.Name ?? $"mensaje-{index + 1}.eml",
        _ => $"adjunto-{index + 1}"
    };
    private static string EncodeMessageKey(string folder, UniqueId uid) => Base64UrlEncode($"{folder}\n{uid.Id}");
    private static MessageKey DecodeMessageKey(string value)
    {
        try
        {
            var decoded = Base64UrlDecode(value);
            var separator = decoded.LastIndexOf('\n');
            if (separator <= 0 || !uint.TryParse(decoded[(separator + 1)..], out var uid)) throw new InvalidOperationException();
            return new MessageKey(decoded[..separator], uid);
        }
        catch { throw new InvalidOperationException("El identificador del mensaje IMAP no es válido."); }
    }
    private static string EncodeCustomFolder(string fullName) => "custom:" + Base64UrlEncode(fullName);
    private static string DecodeCustomFolder(string value) => Base64UrlDecode(value["custom:".Length..]);
    private static string Base64UrlEncode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4);
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
    private static bool IsCanonicalFolder(string folder) => folder.ToLowerInvariant() is "inbox" or "archive" or "sent" or "drafts" or "spam" or "trash";
    private static string FolderAlias(ImapClient client, IMailFolder folder)
    {
        if (string.Equals(folder.FullName, client.Inbox.FullName, StringComparison.OrdinalIgnoreCase)) return "inbox";
        foreach (var pair in new[] { (SpecialFolder.Archive, "archive"), (SpecialFolder.Sent, "sent"), (SpecialFolder.Drafts, "drafts"), (SpecialFolder.Junk, "spam"), (SpecialFolder.Trash, "trash") })
        {
            try { if (string.Equals(client.GetFolder(pair.Item1)?.FullName, folder.FullName, StringComparison.OrdinalIgnoreCase)) return pair.Item2; } catch { }
        }
        return EncodeCustomFolder(folder.FullName);
    }
    private static string? SafeUnsubscribe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var candidate = value.Split(',')[0].Trim().Trim('<', '>');
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.ToString() : null;
    }

    private sealed record MessageKey(string Folder, uint Uid);
    private sealed record ImapSnapshot(string EmailAddress, string DisplayName, string Username, string Password,
        string ImapHost, int ImapPort, string ImapSecurity, string SmtpHost, int SmtpPort, string SmtpSecurity);
}
