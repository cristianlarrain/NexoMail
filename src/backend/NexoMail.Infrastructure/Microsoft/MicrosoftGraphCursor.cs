using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace NexoMail.Infrastructure.Microsoft;

internal static class MicrosoftGraphCursor
{
    public static string Encode(string nextLink)
    {
        _ = Validate(nextLink);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(nextLink));
    }

    public static string Decode(string cursor)
    {
        try
        {
            var nextLink = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor));
            return Validate(nextLink).ToString();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("El cursor de Microsoft Graph no es válido.");
        }
    }

    private static Uri Validate(string nextLink)
    {
        if (!Uri.TryCreate(nextLink, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "graph.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith("/v1.0/me/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("El cursor de Microsoft Graph no es válido.");
        }

        return uri;
    }
}
