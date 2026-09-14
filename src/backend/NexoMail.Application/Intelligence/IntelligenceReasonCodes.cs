namespace NexoMail.Application.Intelligence;

public static class IntelligenceReasonCodes
{
    public const string EmptyConversation = "EMPTY_CONVERSATION";
    public const string DirectRecipient = "DIRECT_RECIPIENT";
    public const string Unread = "UNREAD";
    public const string Automated = "AUTOMATED";
    public const string Bulk = "BULK";
    public const string ListMessage = "LIST_MESSAGE";
    public const string OwnAccountOnly = "OWN_ACCOUNT_ONLY";
    public const string LatestReceived = "LATEST_RECEIVED";
    public const string LatestSentExternal = "LATEST_SENT_EXTERNAL";
    public const string WaitingExternal = "WAITING_EXTERNAL";
    public const string PendingUser = "PENDING_USER";
    public const string Resolved = "RESOLVED";
    public const string Cancelled = "CANCELLED";
    public const string Overdue = "OVERDUE";
    public const string AgeSignal = "AGE_SIGNAL";
    public const string DeadlineSignal = "DEADLINE_SIGNAL";
    public const string SemanticReviewRequired = "SEMANTIC_REVIEW_REQUIRED";
    public const string NotActionable = "NOT_ACTIONABLE";
}
