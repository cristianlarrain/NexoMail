using System.Net;
using System.Net.Mail;

namespace NexoMail.Infrastructure.Imap;

public sealed record ImapConnectionRequest(
    string EmailAddress,
    string DisplayName,
    string Username,
    string Password,
    string ImapHost,
    int ImapPort,
    string ImapSecurity,
    string SmtpHost,
    int SmtpPort,
    string SmtpSecurity)
{
    public ImapConnectionRequest NormalizeAndValidate()
    {
        var email = (EmailAddress ?? string.Empty).Trim();
        try { _ = new MailAddress(email); }
        catch { throw new InvalidOperationException("La dirección de correo no es válida."); }

        var username = (Username ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("Debes indicar el usuario de autenticación.");
        if (string.IsNullOrWhiteSpace(Password)) throw new InvalidOperationException("Debes indicar la contraseña o contraseña de aplicación.");
        var imapHost = ValidateHost(ImapHost);
        var smtpHost = ValidateHost(SmtpHost);
        ValidatePort(ImapPort);
        ValidatePort(SmtpPort);

        return this with
        {
            EmailAddress = email,
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? email : DisplayName.Trim(),
            Username = username,
            ImapHost = imapHost,
            SmtpHost = smtpHost,
            ImapSecurity = NormalizeSecurity(ImapSecurity),
            SmtpSecurity = NormalizeSecurity(SmtpSecurity)
        };
    }

    public static string NormalizeSecurity(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("/", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
        return normalized switch
        {
            "ssl" or "ssltls" or "tls" => "ssl",
            "starttls" => "starttls",
            _ => throw new InvalidOperationException("La seguridad debe ser SSL/TLS o STARTTLS.")
        };
    }

    public static string ValidateHost(string? value)
    {
        var host = (value ?? string.Empty).Trim().TrimEnd('.');
        if (host.Length is < 3 or > 253 || host.Contains('/') || host.Contains('\\') || host.Contains(':'))
            throw new InvalidOperationException("El servidor de correo no es válido.");
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out _))
            throw new InvalidOperationException("Por seguridad, indica un nombre DNS público para el servidor de correo.");
        if (!host.All(character => char.IsLetterOrDigit(character) || character is '.' or '-'))
            throw new InvalidOperationException("El servidor de correo contiene caracteres no válidos.");
        return host;
    }

    public static void ValidatePort(int port)
    {
        if (port is < 1 or > 65535) throw new InvalidOperationException("El puerto del servidor de correo no es válido.");
    }
}
