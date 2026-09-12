namespace NexoMail.Infrastructure.Data;

public sealed class ImapCredentialEntity
{
    public Guid Id { get; set; }
    public Guid MailAccountId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string EncryptedPassword { get; set; } = string.Empty;
    public string ImapHost { get; set; } = string.Empty;
    public int ImapPort { get; set; }
    public string ImapSecurity { get; set; } = "ssl";
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; }
    public string SmtpSecurity { get; set; } = "starttls";
    public DateTimeOffset UpdatedAt { get; set; }
}
