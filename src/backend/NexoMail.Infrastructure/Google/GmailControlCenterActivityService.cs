using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Google;

public sealed class GmailControlCenterActivityService(
    NexoMailDbContext database,
    IUserContext userContext)
{
    private static readonly TimeSpan IndexFreshness = TimeSpan.FromMinutes(20);

    public async Task<ControlCenterActivitySnapshot> GetActivityAsync(Guid? accountId, int? requestedDays, int? requestedOffsetDays, CancellationToken cancellationToken)
    {
        var days = requestedDays is 14 or 30 ? requestedDays.Value : 7;
        var offsetDays = Math.Clamp(requestedOffsetDays ?? 0, 0, 365);
        var now = DateTimeOffset.UtcNow;
        var endDay = now.UtcDateTime.Date.AddDays(-offsetDays);
        var startDay = endDay.AddDays(-(days - 1));
        var startAt = new DateTimeOffset(DateTime.SpecifyKind(startDay, DateTimeKind.Utc));
        var endExclusive = new DateTimeOffset(DateTime.SpecifyKind(endDay.AddDays(1), DateTimeKind.Utc));
        var userId = userContext.UserId;

        var accountQuery = database.MailAccounts
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive && x.Provider == MailProviderType.Gmail);
        if (accountId.HasValue) accountQuery = accountQuery.Where(x => x.Id == accountId.Value);
        var accounts = await accountQuery.OrderBy(x => x.DisplayName).ToArrayAsync(cancellationToken);
        var accountIds = accounts.Select(x => x.Id).ToArray();

        var indexedMessages = accountIds.Length == 0
            ? []
            : await database.MailMessageIndex
                .AsNoTracking()
                .Where(x => x.UserId == userId && accountIds.Contains(x.AccountId))
                .ToArrayAsync(cancellationToken);
        var messages = indexedMessages
            .Where(x => x.OccurredAt >= startAt && x.OccurredAt < endExclusive)
            .ToArray();

        var indexStates = accountIds.Length == 0
            ? []
            : await database.MailIndexStates
                .AsNoTracking()
                .Where(x => x.UserId == userId && accountIds.Contains(x.AccountId))
                .ToArrayAsync(cancellationToken);
        var freshAccounts = indexStates
            .Where(x => now - x.LastIndexedAt <= IndexFreshness)
            .Select(x => x.AccountId)
            .ToHashSet();

        var totals = BuildDays(messages, startDay, days);
        var accountActivities = accounts.Select(account => new ControlCenterAccountActivity(
            account.Id,
            account.DisplayName,
            account.Color,
            freshAccounts.Contains(account.Id),
            BuildDays(messages.Where(x => x.AccountId == account.Id), startDay, days))).ToArray();

        return new ControlCenterActivitySnapshot(
            days,
            offsetDays,
            startDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            endDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            totals,
            accountActivities,
            accounts.Count(x => !freshAccounts.Contains(x.Id)),
            now);
    }

    private static ControlCenterDay[] BuildDays(IEnumerable<MailMessageIndexEntity> source, DateTime startDay, int days)
    {
        var messages = source.ToArray();
        return Enumerable.Range(0, days)
            .Select(offset => startDay.AddDays(offset))
            .Select(day => new ControlCenterDay(
                day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                messages.Count(x => x.IsInbox && !string.Equals(x.Direction, "sent", StringComparison.OrdinalIgnoreCase) && x.OccurredAt.UtcDateTime.Date == day),
                messages.Count(x => string.Equals(x.Direction, "sent", StringComparison.OrdinalIgnoreCase) && x.OccurredAt.UtcDateTime.Date == day)))
            .ToArray();
    }
}
