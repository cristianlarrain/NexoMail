namespace NexoMail.Domain;

public static class CommercialFeaturePolicy
{
    public static string? RequiredEntitlement(string? method, string? path)
    {
        var verb = (method ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedPath = NormalizePath(path);

        if (normalizedPath.StartsWith("/api/mail/ai", StringComparison.Ordinal))
            return CommercialEntitlements.NexiAi;

        if (normalizedPath.StartsWith("/api/mail/control-center/activity", StringComparison.Ordinal))
            return CommercialEntitlements.AdvancedAnalytics;

        if (normalizedPath.StartsWith("/api/mail/control-center/tracking", StringComparison.Ordinal)
            || normalizedPath.StartsWith("/api/mail/control-center/state", StringComparison.Ordinal))
            return CommercialEntitlements.TrackingBasic;

        if (normalizedPath == "/api/mail/control-center"
            || normalizedPath.StartsWith("/api/mail/control-center/", StringComparison.Ordinal))
            return CommercialEntitlements.ControlCenterBasic;

        if (normalizedPath.StartsWith("/api/mail/rules", StringComparison.Ordinal))
            return CommercialEntitlements.MailActions;

        if (verb == "POST" && normalizedPath == "/api/mail/send")
            return CommercialEntitlements.MailActions;

        if (normalizedPath.StartsWith("/api/mail/messages/", StringComparison.Ordinal))
        {
            if ((verb == "POST" && (normalizedPath.EndsWith("/reply", StringComparison.Ordinal)
                                    || normalizedPath.EndsWith("/forward", StringComparison.Ordinal)
                                    || normalizedPath.EndsWith("/trash", StringComparison.Ordinal)
                                    || normalizedPath.EndsWith("/move", StringComparison.Ordinal)))
                || verb == "PATCH")
                return CommercialEntitlements.MailActions;

            return CommercialEntitlements.UnifiedMail;
        }

        if (normalizedPath == "/api/mail/messages" || normalizedPath == "/api/mail/accounts")
            return CommercialEntitlements.UnifiedMail;

        if (normalizedPath.StartsWith("/api/mail/ignored-senders/", StringComparison.Ordinal)
            || normalizedPath.StartsWith("/api/mail/folders/", StringComparison.Ordinal))
            return CommercialEntitlements.MailActions;

        return null;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var value = path.Trim();
        var queryIndex = value.IndexOf('?');
        if (queryIndex >= 0) value = value[..queryIndex];
        return value.TrimEnd('/').ToLowerInvariant();
    }
}
