using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Google;

public sealed class GmailControlCenterService(
    NexoMailDbContext database,
    IUserContext userContext)
{
    private const int LookbackDays = 14;
    private const int ActivityDays = 7;
    private const string ManualTrackingPrefix = "manual:";
    private static readonly TimeSpan IndexFreshness = TimeSpan.FromMinutes(20);

    public async Task<ControlCenterSnapshot> GetSnapshotAsync(Guid? accountId, CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var accountQuery = database.MailAccounts
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive && x.Provider == MailProviderType.Gmail);
        if (accountId.HasValue) accountQuery = accountQuery.Where(x => x.Id == accountId.Value);
        var accounts = await accountQuery.OrderBy(x => x.DisplayName).ToArrayAsync(cancellationToken);
        var accountIds = accounts.Select(x => x.Id).ToArray();

        var stateQuery = database.ControlCenterStates.AsNoTracking().Where(x => x.UserId == userId);
        if (accountId.HasValue) stateQuery = stateQuery.Where(x => x.AccountId == accountId.Value);
        var states = await stateQuery.ToArrayAsync(cancellationToken);

        var indexStates = accountIds.Length == 0
            ? []
            : await database.MailIndexStates
                .AsNoTracking()
                .Where(x => x.UserId == userId && accountIds.Contains(x.AccountId))
                .ToArrayAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var indexStateLookup = indexStates.ToDictionary(x => x.AccountId);
        var availabilityByAccount = accounts.ToDictionary(
            x => x.Id,
            x => AvailabilityStatus(indexStateLookup.GetValueOrDefault(x.Id), now));
        var operationalAccountIds = availabilityByAccount
            .Where(x => string.Equals(x.Value, ControlCenterAvailabilityStatus.Available, StringComparison.Ordinal))
            .Select(x => x.Key)
            .ToArray();

        var unreadAccountIds = operationalAccountIds.Length == 0
            ? []
            : await database.MailMessageIndex
                .AsNoTracking()
                .Where(x => x.UserId == userId && operationalAccountIds.Contains(x.AccountId) && x.IsInbox && x.IsUnread)
                .Select(x => x.AccountId)
                .ToArrayAsync(cancellationToken);

        var lookbackStart = now.AddDays(-LookbackDays);
        var activityStart = now.UtcDateTime.Date.AddDays(-(ActivityDays - 1));
        var queryStart = new DateTimeOffset(DateTime.SpecifyKind(activityStart, DateTimeKind.Utc));
        if (lookbackStart < queryStart) queryStart = lookbackStart;

        var indexedMessages = operationalAccountIds.Length == 0
            ? []
            : await database.MailMessageIndex
                .AsNoTracking()
                .Where(x => x.UserId == userId && operationalAccountIds.Contains(x.AccountId))
                .ToArrayAsync(cancellationToken);
        var messages = indexedMessages
            .Where(x => x.OccurredAt >= queryStart)
            .OrderBy(x => x.OccurredAt)
            .ToArray();

        var accountLookup = accounts.ToDictionary(x => x.Id);
        var stateLookup = states.ToDictionary(x => (x.AccountId, x.ConversationId));
        var pending = new List<PendingRaw>();

        foreach (var thread in messages
                     .Where(x => x.OccurredAt >= lookbackStart)
                     .GroupBy(x => (x.AccountId, x.ThreadId)))
        {
            var latest = thread.OrderByDescending(x => x.OccurredAt).First();
            if (!accountLookup.TryGetValue(latest.AccountId, out var account)) continue;

            PendingRaw? item = null;
            if (string.Equals(latest.Direction, "sent", StringComparison.OrdinalIgnoreCase))
            {
                item = new PendingRaw(
                    account.Id,
                    account.DisplayName,
                    account.Color,
                    latest.ProviderMessageId,
                    latest.ThreadId,
                    "sent",
                    DisplaySentCounterpart(latest.ToAddresses),
                    DisplaySubject(latest.Subject),
                    latest.OccurredAt,
                    true);
            }
            else if (latest.IsInbox && !ControlCenterMessageClassifier.IsNonActionableReceived(latest))
            {
                item = new PendingRaw(
                    account.Id,
                    account.DisplayName,
                    account.Color,
                    latest.ProviderMessageId,
                    latest.ThreadId,
                    "received",
                    DisplayReceivedCounterpart(latest),
                    DisplaySubject(latest.Subject),
                    latest.OccurredAt,
                    !latest.IsUnread);
            }

            if (item is not null && !IsSuppressed(item, stateLookup, now)) pending.Add(item);
        }

        var orderedPending = pending.OrderBy(x => x.Since).ToArray();
        var activity = Enumerable.Range(0, ActivityDays)
            .Select(offset => activityStart.AddDays(offset))
            .Select(day =>
            {
                var received = messages.Count(x => x.IsInbox && !string.Equals(x.Direction, "sent", StringComparison.OrdinalIgnoreCase) && x.OccurredAt.UtcDateTime.Date == day);
                var sent = messages.Count(x => string.Equals(x.Direction, "sent", StringComparison.OrdinalIgnoreCase) && x.OccurredAt.UtcDateTime.Date == day);
                return new ControlCenterDay(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), received, sent);
            })
            .ToArray();

        var priorityItems = orderedPending.Take(6).Select(ToPendingItem).ToArray();
        var pendingItems = orderedPending.Select(ToPendingItem).ToArray();
        var accountSummaries = accounts.Select(account =>
        {
            var availability = availabilityByAccount[account.Id];
            var isAvailable = string.Equals(availability, ControlCenterAvailabilityStatus.Available, StringComparison.Ordinal);
            return new ControlCenterAccountSummary(
                account.Id,
                account.DisplayName,
                account.Color,
                orderedPending.Count(item => item.AccountId == account.Id && item.Direction == "received"),
                orderedPending.Count(item => item.AccountId == account.Id && item.Direction == "sent"),
                unreadAccountIds.Count(id => id == account.Id),
                isAvailable,
                availability);
        }).ToArray();

        return new ControlCenterSnapshot(
            orderedPending.Count(x => x.Direction == "received"),
            orderedPending.Count(x => x.Direction == "sent"),
            unreadAccountIds.Length,
            orderedPending.Count(x => now - x.Since >= TimeSpan.FromHours(48)),
            activity,
            priorityItems,
            pendingItems,
            accountSummaries,
            accountSummaries.Count(x => !x.IsAvailable),
            now);
    }

    public async Task<bool> UpdateStateAsync(Guid accountId, string conversationId, string messageId, string action, int? snoozeHours, CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var accountExists = await database.MailAccounts
            .AsNoTracking()
            .AnyAsync(x => x.Id == accountId && x.UserId == userId && x.IsActive, cancellationToken);
        if (!accountExists) return false;

        var state = await database.ControlCenterStates.SingleOrDefaultAsync(
            x => x.UserId == userId && x.AccountId == accountId && x.ConversationId == conversationId,
            cancellationToken);

        if (action == "active")
        {
            if (state is not null) database.ControlCenterStates.Remove(state);
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }

        if (state is null)
        {
            state = new ControlCenterStateEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AccountId = accountId,
                ConversationId = conversationId
            };
            database.ControlCenterStates.Add(state);
        }

        var now = DateTimeOffset.UtcNow;
        state.LastMessageId = messageId;
        state.UpdatedAt = now;
        if (action == "resolved")
        {
            state.Status = "resolved";
            state.SnoozedUntil = null;

            var manualKey = $"{ManualTrackingPrefix}{messageId.Trim()}";
            var manualState = await database.ControlCenterStates.SingleOrDefaultAsync(
                x => x.UserId == userId && x.AccountId == accountId && x.ConversationId == manualKey,
                cancellationToken);
            if (manualState is not null) database.ControlCenterStates.Remove(manualState);
        }
        else
        {
            state.Status = "snoozed";
            state.SnoozedUntil = now.AddHours(Math.Clamp(snoozeHours ?? 24, 1, 24 * 30));
        }

        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string AvailabilityStatus(MailIndexStateEntity? state, DateTimeOffset now)
    {
        if (state is null) return ControlCenterAvailabilityStatus.Stale;
        if (string.Equals(state.LastSyncErrorCode, ControlCenterAvailabilityStatus.AuthError, StringComparison.OrdinalIgnoreCase))
            return ControlCenterAvailabilityStatus.AuthError;
        if (string.Equals(state.LastSyncErrorCode, ControlCenterAvailabilityStatus.SyncError, StringComparison.OrdinalIgnoreCase))
            return ControlCenterAvailabilityStatus.SyncError;
        return now - state.LastIndexedAt <= IndexFreshness
            ? ControlCenterAvailabilityStatus.Available
            : ControlCenterAvailabilityStatus.Stale;
    }

    private static bool IsSuppressed(PendingRaw item, IReadOnlyDictionary<(Guid AccountId, string ConversationId), ControlCenterStateEntity> states, DateTimeOffset now)
    {
        if (!states.TryGetValue((item.AccountId, item.ConversationId), out var state)) return false;
        if (!string.Equals(state.LastMessageId, item.MessageId, StringComparison.Ordinal)) return false;
        if (string.Equals(state.Status, "resolved", StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(state.Status, "snoozed", StringComparison.OrdinalIgnoreCase) && state.SnoozedUntil is { } until && until > now;
    }

    private static ControlCenterPendingItem ToPendingItem(PendingRaw item) => new(
        item.AccountId,
        item.AccountName,
        item.AccountColor,
        item.MessageId,
        item.ConversationId,
        item.Direction,
        item.Counterpart,
        item.Subject,
        item.Since,
        item.IsRead);

    private static string DisplayReceivedCounterpart(MailMessageIndexEntity message)
    {
        var value = string.IsNullOrWhiteSpace(message.FromName) ? message.FromAddress : message.FromName;
        return DisplayCounterpart(value, "Sin remitente");
    }

    private static string DisplaySentCounterpart(string serializedAddresses)
    {
        if (string.IsNullOrWhiteSpace(serializedAddresses)) return "Sin destinatario";
        var first = serializedAddresses.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first)) return "Sin destinatario";
        var fields = first.Split('\t', 2);
        var value = fields.Length == 2 && !string.IsNullOrWhiteSpace(fields[0]) ? fields[0] : fields.Last();
        return DisplayCounterpart(value, "Sin destinatario");
    }

    private static string DisplayCounterpart(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var clean = Regex.Replace(value.Trim(), "\\s+", " ");
        return clean.Length <= 90 ? clean : clean[..87] + "…";
    }

    private static string DisplaySubject(string value)
    {
        var subject = string.IsNullOrWhiteSpace(value) ? "(sin asunto)" : value.Trim();
        return subject.Length <= 120 ? subject : subject[..117] + "…";
    }

    private sealed record PendingRaw(
        Guid AccountId,
        string AccountName,
        string AccountColor,
        string MessageId,
        string ConversationId,
        string Direction,
        string Counterpart,
        string Subject,
        DateTimeOffset Since,
        bool IsRead);
}
