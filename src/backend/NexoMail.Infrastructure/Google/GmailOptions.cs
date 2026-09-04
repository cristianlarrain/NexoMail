namespace NexoMail.Infrastructure.Google;

public sealed class GmailOptions
{
    public const string SectionName = "Google";
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = "http://localhost:5052/api/oauth/google/callback";
    public string FrontendUrl { get; init; } = "http://localhost:5173/settings/accounts";
}
