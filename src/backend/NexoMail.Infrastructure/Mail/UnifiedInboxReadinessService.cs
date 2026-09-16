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

public sealed record UnifiedInboxReadiness(
    bool IsReady,
    IReadOnlyCollection<Guid> IncompleteAccountIds,
    IReadOnlyCollection<Guid> UnsupportedProviderAccountIds);

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
            .Select(x => new { x.Id, x.Provider })
            .ToArrayAsync(cancellationToken);

        var unsupported = accounts
            .Where(x => x.Provider != MailProviderType.Gmail)
            .Select(x => x.Id)
            .OrderBy(x => x)
            .ToArray();

        Guid[] incomplete = [];
        if (options.Value.RequireCompleteBackfill)
        {
            var gmailAccountIds = accounts
                .Where(x => x.Provider == MailProviderType.Gmail)
                .Select(x => x.Id)
                .OrderBy(x => x)
                .ToArray();

            if (gmailAccountIds.Length > 0)
            {
                var completed = await database.MailIndexStates
                    .AsNoTracking()
                    .Where(x => x.UserId == userId
                        && gmailAccountIds.Contains(x.AccountId)
                        && x.BackfillCompletedAt != null)
                    .Select(x => x.AccountId)
                    .ToArrayAsync(cancellationToken);
                var completedSet = completed.ToHashSet();
                incomplete = gmailAccountIds.Where(x => !completedSet.Contains(x)).ToArray();
            }
        }

        return new UnifiedInboxReadiness(
            incomplete.Length == 0 && unsupported.Length == 0,
            incomplete,
            unsupported);
    }
}
