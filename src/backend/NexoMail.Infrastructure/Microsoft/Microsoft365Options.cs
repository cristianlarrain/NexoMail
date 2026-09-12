namespace NexoMail.Infrastructure.Microsoft;

public sealed class Microsoft365Options
{
    public const string SectionName = "Microsoft365";
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = "http://localhost:5052/api/oauth/microsoft/callback";
    public string FrontendUrl { get; init; } = "http://localhost:5173/settings/accounts";
    public string AuthorityTenant { get; init; } = "organizations";
}
