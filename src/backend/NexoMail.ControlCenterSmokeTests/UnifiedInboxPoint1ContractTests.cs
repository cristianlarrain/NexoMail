using NexoMail.Infrastructure.Data;

namespace NexoMail.ControlCenterSmokeTests;

internal static class UnifiedInboxPoint1ContractTests
{
    // Compile-time contract for Point 1. This intentionally starts RED until
    // MailMessageIndex exposes provider-independent mailbox membership flags.
    public static void AssertNormalizedMailboxMembershipContract()
    {
        var indexed = new MailMessageIndexEntity();

        if (indexed.IsSent || indexed.IsDraft || indexed.IsSpam || indexed.IsTrash)
            throw new InvalidOperationException("Los estados normalizados deben partir en false.");
    }
}
