using NexoMail.Domain;

namespace NexoMail.Application;

public enum MailRuleActionType
{
    Trash,
    Archive,
    MarkRead,
    MoveToFolder
}

public sealed record MailRuleDestination(string Id, string DisplayName);

public sealed record MailRuleDefinition(
    string RuleId,
    string Query,
    MailRuleActionType Action,
    string? DestinationId = null,
    string? DestinationName = null);

public sealed record MailRuleCreateRequest(
    Guid AccountId,
    string Query,
    MailRuleActionType Action,
    string? DestinationId = null,
    string? DestinationName = null);

public sealed record MailRuleCreateResult(
    MailRuleDefinition Rule,
    bool Created);

/// <summary>
/// Provider-neutral contract for server-side mail rules. Gmail filters and
/// Microsoft Graph inbox rules implement the same NexoMail rule model.
/// </summary>
public interface IMailRuleProvider
{
    MailProviderType ProviderType { get; }
    Task<IReadOnlyList<MailRuleDefinition>> ListAsync(Guid accountId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MailRuleDestination>> GetDestinationsAsync(Guid accountId, CancellationToken cancellationToken);
    Task<MailRuleCreateResult> CreateAsync(MailRuleCreateRequest request, CancellationToken cancellationToken);
    Task<bool> RemoveAsync(Guid accountId, string ruleId, CancellationToken cancellationToken);
}
