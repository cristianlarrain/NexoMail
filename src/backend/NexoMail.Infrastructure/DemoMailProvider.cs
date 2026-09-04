using NexoMail.Application;
using NexoMail.Domain;

namespace NexoMail.Infrastructure;

public sealed class DemoMailProvider : IMailProvider
{
    public MailProviderType ProviderType => MailProviderType.Demo;
    private readonly List<MailMessage> _messages = DemoData.CreateMessages();

    public Task<PagedResult<MailSummary>> GetMessagesAsync(MailQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = _messages
            .Where(m => (!query.AccountId.HasValue || m.AccountId == query.AccountId) && m.FolderId == query.FolderId)
            .Where(m => string.IsNullOrWhiteSpace(query.Search) || $"{m.From.Name} {m.From.Address} {m.Subject} {m.Preview}".Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.ReceivedAt)
            .Take(Math.Clamp(query.Take, 1, 50))
            .Select(m => new MailSummary(m.ProviderMessageId, m.AccountId, m.From.Name, m.From.Address, m.Subject, m.Preview, m.ReceivedAt, m.IsRead, m.Attachments.Count > 0, m.FolderId))
            .ToArray();
        return Task.FromResult(new PagedResult<MailSummary>(items));
    }

    public Task<MailMessage?> GetMessageAsync(Guid accountId, string messageId, CancellationToken cancellationToken) =>
        Task.FromResult(_messages.SingleOrDefault(m => m.AccountId == accountId && m.ProviderMessageId == messageId));

    public Task<MailAttachmentContent?> GetAttachmentAsync(Guid accountId, string messageId, string attachmentId, CancellationToken cancellationToken)
    {
        var attachment = _messages.SingleOrDefault(m => m.AccountId == accountId && m.ProviderMessageId == messageId)?.Attachments.SingleOrDefault(x => x.Id == attachmentId);
        return Task.FromResult(attachment is null ? null : new MailAttachmentContent(System.Text.Encoding.UTF8.GetBytes("Vista previa disponible al conectar una cuenta Gmail."), "text/plain; charset=utf-8", attachment.Name));
    }

    public Task SendAsync(ComposeMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ReplyAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ReplyAllAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ForwardAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task MarkReadAsync(Guid accountId, string messageId, bool read, CancellationToken cancellationToken)
    {
        var index = _messages.FindIndex(m => m.AccountId == accountId && m.ProviderMessageId == messageId);
        if (index >= 0) _messages[index] = _messages[index] with { IsRead = read };
        return Task.CompletedTask;
    }

    public Task EmptyFolderAsync(Guid accountId, string folderId, CancellationToken cancellationToken)
    {
        if (folderId == "trash") _messages.RemoveAll(x => x.AccountId == accountId && x.FolderId == "trash");
        return Task.CompletedTask;
    }

    public Task MoveToTrashAsync(Guid accountId, string messageId, CancellationToken cancellationToken) => MoveToFolderAsync(accountId, messageId, "trash", cancellationToken);

    public Task MoveToFolderAsync(Guid accountId, string messageId, string folderId, CancellationToken cancellationToken)
    {
        if (folderId is not ("inbox" or "trash")) throw new InvalidOperationException("Esta carpeta no admite movimiento de mensajes.");
        var index = _messages.FindIndex(x => x.AccountId == accountId && x.ProviderMessageId == messageId);
        if (index >= 0) _messages[index] = _messages[index] with { FolderId = folderId };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<MailFolder>> GetFoldersAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<MailFolder>>([new("inbox", "Bandeja de entrada", _messages.Count(m => m.AccountId == accountId && !m.IsRead)), new("sent", "Enviados", 0), new("drafts", "Borradores", 0), new("trash", "Papelera", 0)]);
}

public sealed class DemoMailGateway(IEnumerable<IMailProvider> providers) : IMailGateway
{
    private readonly IMailProvider _demo = providers.Single(p => p.ProviderType == MailProviderType.Demo);
    private readonly List<MailAccount> _accounts = DemoData.Accounts.ToList();
    public Task<IReadOnlyCollection<MailAccount>> GetAccountsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<MailAccount>>(_accounts);
    public Task<MailAccount?> UpdateAccountAsync(Guid accountId, MailAccountSettings settings, CancellationToken cancellationToken)
    {
        var index = _accounts.FindIndex(x => x.Id == accountId);
        if (index < 0) return Task.FromResult<MailAccount?>(null);
        _accounts[index] = _accounts[index] with { DisplayName = settings.DisplayName, Color = settings.Color };
        return Task.FromResult<MailAccount?>(_accounts[index]);
    }
    public Task<PagedResult<MailSummary>> GetMessagesAsync(MailQuery query, CancellationToken cancellationToken) => _demo.GetMessagesAsync(query, cancellationToken);
    public Task<MailMessage?> GetMessageAsync(Guid accountId, string messageId, CancellationToken cancellationToken) => _demo.GetMessageAsync(accountId, messageId, cancellationToken);
    public Task<MailAttachmentContent?> GetAttachmentAsync(Guid accountId, string messageId, string attachmentId, CancellationToken cancellationToken) => _demo.GetAttachmentAsync(accountId, messageId, attachmentId, cancellationToken);
    public Task SendAsync(ComposeMessage message, CancellationToken cancellationToken) => _demo.SendAsync(message, cancellationToken);
    public Task ReplyAsync(Guid accountId, string messageId, ComposeMessage message, bool replyAll, CancellationToken cancellationToken) => replyAll ? _demo.ReplyAllAsync(accountId, messageId, message, cancellationToken) : _demo.ReplyAsync(accountId, messageId, message, cancellationToken);
    public Task ForwardAsync(Guid accountId, string messageId, ComposeMessage message, CancellationToken cancellationToken) => _demo.ForwardAsync(accountId, messageId, message, cancellationToken);
    public Task MarkReadAsync(Guid accountId, string messageId, bool read, CancellationToken cancellationToken) => _demo.MarkReadAsync(accountId, messageId, read, cancellationToken);
    public Task MoveToTrashAsync(Guid accountId, string messageId, CancellationToken cancellationToken) => _demo.MoveToTrashAsync(accountId, messageId, cancellationToken);
    public Task MoveToFolderAsync(Guid accountId, string messageId, string folderId, CancellationToken cancellationToken) => _demo.MoveToFolderAsync(accountId, messageId, folderId, cancellationToken);
    public async Task EmptyFolderAsync(Guid? accountId, string folderId, CancellationToken cancellationToken)
    {
        var accounts = accountId.HasValue ? _accounts.Where(x => x.Id == accountId.Value) : _accounts;
        await Task.WhenAll(accounts.Select(account => _demo.EmptyFolderAsync(account.Id, folderId, cancellationToken)));
    }
}

internal static class DemoData
{
    public static readonly IReadOnlyCollection<MailAccount> Accounts = [
        new(Guid.Parse("4f03b9de-31a5-43dd-a446-f0dcae434001"), MailProviderType.MicrosoftGraph, "cristian@empresa.cl", "Trabajo", "#0f6b78"),
        new(Guid.Parse("4f03b9de-31a5-43dd-a446-f0dcae434002"), MailProviderType.Gmail, "cristian.nexo@gmail.com", "Gmail", "#c6524b"),
        new(Guid.Parse("4f03b9de-31a5-43dd-a446-f0dcae434003"), MailProviderType.MicrosoftGraph, "c.ramirez@outlook.com", "Outlook", "#486dca")];

    public static List<MailMessage> CreateMessages()
    {
        var senders = new[] { ("Claudio Muñoz", "claudio@empresa.cl"), ("Universidad del Centro", "informaciones@universidad.cl"), ("Banco Horizonte", "notificaciones@bancohorizonte.cl"), ("Manuel Rojas", "manuel.rojas@empresa.cl"), ("Equipo de Producto", "producto@atelier.cl"), ("Camila Torres", "camila@gmail.com") };
        var subjects = new[] { "Reunión de proyecto", "Información de clases", "Resumen de movimientos", "Propuesta actualizada", "Planificación de septiembre", "Documentos para revisión", "Coordinación de equipo", "Tu comprobante está disponible", "Comentarios sobre la presentación", "Seguimiento de pendientes" };
        return Enumerable.Range(0, 21).Select(i =>
        {
            var sender = senders[i % senders.Length]; var account = Accounts.ElementAt(i % Accounts.Count);
            var subject = subjects[i % subjects.Length]; var attachment = i % 4 == 0 ? new[] { new MailAttachment($"att-{i}", i % 8 == 0 ? "propuesta.pdf" : "detalle.xlsx", "application/octet-stream", 124_000 + i * 200) } : Array.Empty<MailAttachment>();
            var received = DateTimeOffset.UtcNow.AddMinutes(-i * 47).AddDays(-(i / 8));
            return new MailMessage($"demo-{i + 1}", account.Id, new MailAddress(sender.Item1, sender.Item2), [new MailAddress(account.DisplayName, account.EmailAddress)], [], subject, $"<p>Hola Cristian,</p><p>Te compartimos la información relacionada con <strong>{subject.ToLowerInvariant()}</strong>. Revisa los antecedentes y cuéntanos si necesitas algo más.</p><p>Saludos,<br>{sender.Item1}</p>", "Te compartimos la información relacionada con este tema. Revisa los antecedentes adjuntos.", received, i % 3 != 0, attachment, "inbox");
        }).ToList();
    }
}
