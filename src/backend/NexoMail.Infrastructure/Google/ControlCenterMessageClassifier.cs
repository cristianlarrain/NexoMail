using System.Globalization;
using System.Text;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Google;

public readonly record struct ControlCenterMessageMetadata(
    string FromAddress,
    string Subject,
    string GmailLabels,
    string AutoSubmitted,
    string Precedence,
    bool HasListUnsubscribe);

public static class ControlCenterMessageClassifier
{
    public static bool IsNonActionableReceived(MailMessageIndexEntity message) =>
        IsNonActionableReceived(new ControlCenterMessageMetadata(
            message.FromAddress,
            message.Subject,
            message.GmailLabels,
            message.AutoSubmitted,
            message.Precedence,
            message.HasListUnsubscribe));

    public static bool IsNonActionableReceived(ControlCenterMessageMetadata message)
    {
        var labels = ParseLabels(message.GmailLabels);
        if (labels.Contains("CATEGORY_PROMOTIONS") || labels.Contains("CATEGORY_SOCIAL") || labels.Contains("CATEGORY_FORUMS")) return true;
        if (IsLikelyAutomated(message)) return true;
        if (IsHighConfidenceTransactionalSubject(message.Subject)) return true;

        var updateCategory = labels.Contains("CATEGORY_UPDATES");
        var notificationSender = HasNotificationSenderSignal(message.FromAddress);
        return (updateCategory || notificationSender) && IsInformationalTransactionalSubject(message.Subject);
    }

    private static HashSet<string> ParseLabels(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsLikelyAutomated(ControlCenterMessageMetadata message)
    {
        var from = message.FromAddress.ToLowerInvariant();
        if (from.Contains("no-reply") || from.Contains("noreply") || from.Contains("do-not-reply") || from.Contains("donotreply") || from.Contains("mailer-daemon")) return true;
        if (HasNotificationSenderSignal(message.FromAddress)) return true;
        if (message.HasListUnsubscribe) return true;
        if (!string.IsNullOrWhiteSpace(message.AutoSubmitted) && !string.Equals(message.AutoSubmitted, "no", StringComparison.OrdinalIgnoreCase)) return true;
        return message.Precedence.Equals("bulk", StringComparison.OrdinalIgnoreCase) ||
               message.Precedence.Equals("list", StringComparison.OrdinalIgnoreCase) ||
               message.Precedence.Equals("junk", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasNotificationSenderSignal(string from)
    {
        var value = NormalizeForMatch(from);
        string[] signals =
        [
            "notificaciones@", "notificacion@", "notifications@", "notification@",
            "alertas@", "alerta@", "alerts@", "alert@",
            "facturacion@", "facturas@", "billing@", "invoice@", "invoices@",
            "comprobantes@", "recibos@", "receipts@", "pedidos@", "orders@",
            "despachos@", "shipping@", "seguridad@", "security@", "avisos@", "updates@"
        ];
        return signals.Any(value.Contains);
    }

    private static bool IsHighConfidenceTransactionalSubject(string subject)
    {
        var value = NormalizeForMatch(subject);
        string[] phrases =
        [
            "comprobante de pago", "comprobante de transferencia", "comprobante de compra",
            "confirmacion de compra", "confirmacion de pago", "confirmacion de transferencia",
            "compra realizada", "compra aprobada", "pago realizado", "pago recibido", "pago procesado",
            "transferencia realizada", "transferencia recibida", "factura electronica", "boleta electronica",
            "recibo de pago", "estado de cuenta", "cartola bancaria", "movimiento en tu cuenta", "movimiento en su cuenta",
            "pedido confirmado", "orden confirmada", "despacho confirmado", "envio confirmado", "entrega confirmada",
            "codigo de verificacion", "clave temporal", "inicio de sesion", "alerta de seguridad",
            "cargo realizado", "abono recibido", "suscripcion renovada",
            "order confirmation", "payment confirmation", "login code", "verification code", "security code",
            "verify your email", "email verification", "verifica tu correo", "confirma tu correo",
            "payment details have been changed", "successfully set up", "change dns entries",
            "add credits", "running low", "actualiza tu app", "nueva encomienda recibida", "encomienda retirada",
            "encuesta de satisfaccion"
        ];
        return phrases.Any(value.Contains);
    }

    private static bool IsInformationalTransactionalSubject(string subject)
    {
        var value = NormalizeForMatch(subject);
        string[] terms =
        [
            "comprobante", "factura", "boleta", "recibo", "pago", "compra", "pedido", "orden",
            "despacho", "envio", "entrega", "transferencia", "transaccion", "movimiento", "estado de cuenta",
            "cartola", "codigo", "verificacion", "seguridad", "alerta", "suscripcion", "resumen de actividad",
            "confirmation", "receipt", "payment", "order", "shipping", "delivery", "verification", "security",
            "subscription", "login code"
        ];
        return terms.Any(value.Contains);
    }

    private static string NormalizeForMatch(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
