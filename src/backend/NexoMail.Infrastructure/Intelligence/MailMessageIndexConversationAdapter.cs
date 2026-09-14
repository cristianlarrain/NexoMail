using NexoMail.Application.Intelligence;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Intelligence;

public sealed class MailMessageIndexConversationAdapter
{
    public IReadOnlyList<CommunicationConversation> Adapt(
        IReadOnlyCollection<MailMessageIndexEntity> messages,
        IReadOnlyCollection<string> ownAddresses,
        DateTimeOffset evaluatedAt) => [];
}
