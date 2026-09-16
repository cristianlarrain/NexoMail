using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Mail;

public sealed class UnifiedInboxOptions
{
    public const string SectionName = "UnifiedInbox";
    public bool IndexReadEnabled { get; set; }
    public bool RequireCompleteBackfill { get; set; } = true;
}

public sealed record UnifiedInboxAccountReadiness(
    Guid AccountId,
    string DisplayName,
    string EmailAddress,
    DateTimeOffset? BackfillStartedAt,
    DateTimeOffset? BackfillCompletedAt,
    bool HasBackfillPageToken,
    bool HasGmailHistoryId,
    DateTimeOffset? LastSyncAttemptAt,
    string? LastSyncErrorCode,
    int IndexedMessageCount,
    DateTimeOffset? SyncLeaseUntil);

public sealed record UnifiedInboxReadiness(
    bool IsReady,
    IReadOnlyCollection<Guid> IncompleteAccountIds,
    IReadOnlyCollection<Guid> UnsupportedProviderAccountIds,
    IReadOnlyCollection<UnifiedInboxAccountReadiness> GmailAccountStates);

public sealed class UnifiedInboxReadinessService(
    NexoMailDbContext database,
    IUserContext userContext,
    IOptions<UnifiedInboxOptions> options)
{
    public async Task<UnifiedInboxReadiness> GetAsync(CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var accounts = await database.MailAccounts
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive)
            .Select(x => new { x.Id, x.Provider, x.DisplayName, x.EmailAddress })
            .ToArrayAsync(cancellationToken);

        var unsupported = accounts
            .Where(x => x.Provider != MailProviderType.Gmail)
            .Select(x => x.Id)
            .OrderBy(x => x)
            .ToArray();

        var gmailAccounts = accounts
            .Where(x => x.Provider == MailProviderType.Gmail)
            .OrderBy(x => x.Id)
            .ToArray();
        var gmailAccountIds = gmailAccounts.Select(x => x.Id).ToArray();

        var states = gmailAccountIds.Length == 0
            ? []
            : await database.MailIndexStates
                .AsNoTracking()
                .Where(x => x.UserId == userId && gmailAccountIds.Contains(x.AccountId))
                .ToArrayAsync(cancellationToken);
        var stateByAccountId = states.ToDictionary(x => x.AccountId);

        var gmailAccountStates = gmailAccounts.Select(account =>
        {
            stateByAccountId.TryGetValue(account.Id, out var state);
            return new UnifiedInboxAccountReadiness(
                account.Id,
                account.DisplayName,
                account.EmailAddress,
                state?.BackfillStartedAt,
                state?.BackfillCompletedAt,
                !string.IsNullOrWhiteSpace(state?.BackfillPageToken),
                !string.IsNullOrWhiteSpace(state?.GmailHistoryId),
                state?.LastSyncAttemptAt,
                state?.LastSyncErrorCode,
                state?.IndexedMessageCount ?? 0,
                state?.SyncLeaseUntil);
        }).ToArray();

        Guid[] incomplete = [];
        if (options.Value.RequireCompleteBackfill)
        {
            incomplete = gmailAccountStates
                .Where(x => !x.BackfillCompletedAt.HasValue)
                .Select(x => x.AccountId)
                .ToArray();
        }

        return new UnifiedInboxReadiness(
            incomplete.Length == 0 && unsupported.Length == 0,
            incomplete,
            unsupported,
            gmailAccountStates);
    }
}
