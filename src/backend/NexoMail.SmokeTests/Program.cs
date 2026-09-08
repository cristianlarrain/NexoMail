using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure;

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var cancellationToken = CancellationToken.None;
var provider = new DemoMailProvider();
IMailGateway gateway = new DemoMailGateway(new IMailProvider[] { provider });
var account = (await gateway.GetAccountsAsync(cancellationToken))
    .First(item => item.Provider == MailProviderType.Gmail);

var initialDrafts = await gateway.GetMessagesAsync(
    new MailQuery(account.Id, "drafts", 50), cancellationToken);

var initialMessage = new ComposeMessage(
    account.Id,
    ["destinatario@nexomail.test"],
    ["copia@nexomail.test"],
    [],
    "NexoMail smoke draft",
    "<p>Versión inicial del borrador.</p>",
    [new OutgoingAttachment("inicial.txt", "text/plain", Convert.ToBase64String("inicial"u8.ToArray()))]);

await gateway.SaveDraftAsync(account.Id, null, initialMessage, cancellationToken);

var afterCreate = await gateway.GetMessagesAsync(
    new MailQuery(account.Id, "drafts", 50), cancellationToken);
Ensure(afterCreate.Items.Count == initialDrafts.Items.Count + 1,
    "Crear el borrador no incrementó la carpeta Borradores exactamente en uno.");

var createdSummary = afterCreate.Items.Single(item => item.Subject == initialMessage.Subject);
var created = await gateway.GetMessageAsync(account.Id, createdSummary.ProviderMessageId, cancellationToken)
    ?? throw new InvalidOperationException("El borrador recién creado no pudo abrirse.");
Ensure(created.FolderId == "drafts", "El mensaje creado no quedó en Borradores.");
Ensure(created.To.Single().Address == "destinatario@nexomail.test", "El destinatario inicial no se conservó.");
Ensure(created.Cc.Single().Address == "copia@nexomail.test", "El CC inicial no se conservó.");
Ensure(created.Attachments.Count == 1, "El adjunto inicial no se conservó.");

var editedMessage = new ComposeMessage(
    account.Id,
    ["destinatario@nexomail.test", "segundo@nexomail.test"],
    ["copia-editada@nexomail.test"],
    [],
    "NexoMail smoke draft EDITADO",
    "<p>Versión editada y guardada del borrador.</p>",
    [
        new OutgoingAttachment("inicial.txt", "text/plain", Convert.ToBase64String("inicial"u8.ToArray())),
        new OutgoingAttachment("nuevo.txt", "text/plain", Convert.ToBase64String("nuevo"u8.ToArray()))
    ]);

await gateway.UpdateDraftAsync(account.Id, created.ProviderMessageId, editedMessage, cancellationToken);

var afterUpdate = await gateway.GetMessagesAsync(
    new MailQuery(account.Id, "drafts", 50), cancellationToken);
Ensure(afterUpdate.Items.Count == afterCreate.Items.Count,
    "Guardar una edición creó un borrador duplicado.");
Ensure(afterUpdate.Items.Count(item => item.ProviderMessageId == created.ProviderMessageId) == 1,
    "El borrador editado dejó de ser una única instancia.");

var reopened = await gateway.GetMessageAsync(account.Id, created.ProviderMessageId, cancellationToken)
    ?? throw new InvalidOperationException("El borrador editado no pudo reabrirse.");
Ensure(reopened.Subject == editedMessage.Subject, "El asunto editado no se guardó.");
Ensure(reopened.HtmlBody == editedMessage.HtmlBody, "El cuerpo editado no se guardó.");
Ensure(reopened.To.Select(item => item.Address).SequenceEqual(editedMessage.To),
    "Los destinatarios editados no se guardaron.");
Ensure(reopened.Cc.Select(item => item.Address).SequenceEqual(editedMessage.Cc),
    "El CC editado no se guardó.");
Ensure(reopened.Attachments.Count == 2, "Los adjuntos editados no se conservaron.");

await gateway.SendDraftAsync(account.Id, reopened.ProviderMessageId, editedMessage, cancellationToken);

var afterSend = await gateway.GetMessagesAsync(
    new MailQuery(account.Id, "drafts", 50), cancellationToken);
Ensure(afterSend.Items.Count == initialDrafts.Items.Count,
    "Enviar el borrador no lo retiró de Borradores.");
Ensure(afterSend.Items.All(item => item.ProviderMessageId != reopened.ProviderMessageId),
    "El borrador enviado todavía aparece en Borradores.");

Console.WriteLine("PASS: crear -> abrir -> editar -> guardar sin duplicar -> reabrir -> enviar");
