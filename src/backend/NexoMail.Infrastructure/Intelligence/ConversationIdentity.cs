namespace NexoMail.Infrastructure.Intelligence;

public static class ConversationIdentity
{
    public static string Normalize(Guid accountId, string? conversationId)
    {
        var value = conversationId?.Trim() ?? string.Empty;
        var prefix = $"{accountId:N}:";

        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value[prefix.Length..]
            : value;
    }
}
