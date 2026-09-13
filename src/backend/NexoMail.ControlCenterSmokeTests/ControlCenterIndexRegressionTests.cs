using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;

namespace NexoMail.ControlCenterSmokeTests;

internal static class ControlCenterIndexRegressionTests
{
    public static void RunClassifierRegressionTests()
    {
        var personal = Message("persona@example.com", "Necesito tu respuesta");
        Ensure(!ControlCenterMessageClassifier.IsNonActionableReceived(personal), "Un correo personal no debe excluirse.");

        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("persona@example.com", "Oferta", labels: "INBOX,CATEGORY_PROMOTIONS")), "Promociones debe excluirse.");
        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("noreply@example.com", "Aviso")), "noreply debe excluirse.");
        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("persona@example.com", "Aviso", autoSubmitted: "auto-generated")), "Auto-Submitted debe excluirse.");
        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("persona@example.com", "Aviso", precedence: "bulk")), "Precedence bulk debe excluirse.");
        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("persona@example.com", "Aviso", hasListUnsubscribe: true)), "List-Unsubscribe debe excluirse.");
        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("persona@example.com", "Comprobante de pago")), "Asunto transaccional de alta confianza debe excluirse.");
        Ensure(ControlCenterMessageClassifier.IsNonActionableReceived(Message("alertas@empresa.cl", "Resumen de actividad", labels: "INBOX,CATEGORY_UPDATES")), "CATEGORY_UPDATES con remitente de notificación debe excluirse.");
    }

    public static void RunEntityMetadataContractTests()
    {
        var entity = new MailMessageIndexEntity
        {
            GmailLabels = "INBOX,UNREAD,CATEGORY_UPDATES",
            AutoSubmitted = "auto-generated",
            Precedence = "bulk",
            HasListUnsubscribe = true,
            IsUnread = true,
            IsInbox = true
        };

        Ensure(entity.IsUnread, "El índice debe persistir estado UNREAD.");
        Ensure(entity.IsInbox, "El índice debe persistir estado INBOX.");
        Ensure(entity.GmailLabels.Contains("CATEGORY_UPDATES", StringComparison.Ordinal), "El índice debe persistir categorías Gmail.");
        Ensure(entity.AutoSubmitted == "auto-generated", "El índice debe persistir Auto-Submitted.");
        Ensure(entity.Precedence == "bulk", "El índice debe persistir Precedence.");
        Ensure(entity.HasListUnsubscribe, "El índice debe persistir presencia de List-Unsubscribe.");
    }

    private static ControlCenterMessageMetadata Message(
        string from,
        string subject,
        string labels = "INBOX",
        string autoSubmitted = "",
        string precedence = "",
        bool hasListUnsubscribe = false) =>
        new(from, subject, labels, autoSubmitted, precedence, hasListUnsubscribe);

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
