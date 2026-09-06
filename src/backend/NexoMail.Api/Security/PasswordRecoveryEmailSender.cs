using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace NexoMail.Api.Security;

public interface IPasswordRecoveryEmailSender
{
    Task<bool> SendCodeAsync(string recipientEmail, string recipientName, string verificationCode, CancellationToken ct);
    Task<bool> SendVerificationCodeAsync(string recipientEmail, string recipientName, string verificationCode, CancellationToken ct);
}

public sealed class RecoveryEmailOptions
{
    public const string SectionName = "RecoveryEmail";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "NexoMail";
}

public sealed class SmtpPasswordRecoveryEmailSender(
    IOptions<RecoveryEmailOptions> configuredOptions,
    ILogger<SmtpPasswordRecoveryEmailSender> logger) : IPasswordRecoveryEmailSender
{
    public Task<bool> SendCodeAsync(string recipientEmail, string recipientName, string verificationCode, CancellationToken ct) =>
        SendAsync(
            recipientEmail,
            recipientName,
            "Código de recuperación de NexoMail",
            BuildRecoveryBody(recipientName, verificationCode),
            "recuperación",
            ct);

    public Task<bool> SendVerificationCodeAsync(string recipientEmail, string recipientName, string verificationCode, CancellationToken ct) =>
        SendAsync(
            recipientEmail,
            recipientName,
            "Verifica tu correo en NexoMail",
            BuildVerificationBody(recipientName, verificationCode),
            "verificación",
            ct);

    private async Task<bool> SendAsync(
        string recipientEmail,
        string recipientName,
        string subject,
        string body,
        string purpose,
        CancellationToken ct)
    {
        var options = configuredOptions.Value;
        if (string.IsNullOrWhiteSpace(options.Host) || string.IsNullOrWhiteSpace(options.FromAddress))
        {
            logger.LogWarning("El correo de {Purpose} no está configurado. Falta RecoveryEmail:Host o RecoveryEmail:FromAddress.", purpose);
            return false;
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(options.FromAddress, options.FromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            message.To.Add(new MailAddress(recipientEmail, recipientName));

            using var client = new SmtpClient(options.Host, options.Port)
            {
                EnableSsl = options.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Timeout = 15_000
            };

            if (!string.IsNullOrWhiteSpace(options.UserName))
                client.Credentials = new NetworkCredential(options.UserName, options.Password);

            ct.ThrowIfCancellationRequested();
            await client.SendMailAsync(message, ct);
            logger.LogInformation("Código de {Purpose} enviado por correo a {RecipientEmail}.", purpose, recipientEmail);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is SmtpException or InvalidOperationException or FormatException)
        {
            logger.LogError(exception, "No fue posible enviar el correo de {Purpose} a {RecipientEmail}.", purpose, recipientEmail);
            return false;
        }
    }

    private static string BuildRecoveryBody(string recipientName, string verificationCode) => BuildBody(
        recipientName,
        verificationCode,
        "Recibimos una solicitud para restablecer la contraseña de tu cuenta NexoMail.",
        "Si no solicitaste este cambio, puedes ignorar este mensaje. No compartas este código con nadie.");

    private static string BuildVerificationBody(string recipientName, string verificationCode) => BuildBody(
        recipientName,
        verificationCode,
        "Gracias por crear tu cuenta NexoMail. Para activarla, confirma que esta dirección de correo te pertenece.",
        "Si no creaste una cuenta NexoMail con este correo, puedes ignorar este mensaje.");

    private static string BuildBody(string recipientName, string verificationCode, string introduction, string footer)
    {
        var safeName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(recipientName) ? "usuario" : recipientName);
        var safeCode = WebUtility.HtmlEncode(verificationCode);
        var safeIntroduction = WebUtility.HtmlEncode(introduction);
        var safeFooter = WebUtility.HtmlEncode(footer);
        return $"""
            <!doctype html>
            <html lang="es">
            <body style="margin:0;background:#f5f7f7;font-family:Arial,sans-serif;color:#172125">
              <div style="max-width:520px;margin:32px auto;padding:28px;background:#ffffff;border:1px solid #e2e7e8;border-radius:12px">
                <div style="font-size:20px;font-weight:700;color:#0f6b78;margin-bottom:20px">NexoMail</div>
                <p>Hola {safeName},</p>
                <p>{safeIntroduction}</p>
                <p>Tu código de verificación es:</p>
                <div style="margin:24px 0;padding:16px;text-align:center;background:#eff3f3;border-radius:9px;font-size:30px;font-weight:700;letter-spacing:8px">{safeCode}</div>
                <p>Este código vence en <strong>10 minutos</strong>.</p>
                <p style="color:#687579;font-size:13px">{safeFooter}</p>
              </div>
            </body>
            </html>
            """;
    }
}
