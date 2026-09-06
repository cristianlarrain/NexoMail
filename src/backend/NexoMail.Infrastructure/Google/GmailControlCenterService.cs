using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Google;

public sealed class GmailControlCenterService(
    IHttpClientFactory httpClientFactory,
    NexoMailDbContext database,
    ITokenProtector tokenProtector,
    IOptions<GmailOptions> options,
    IUserContext userContext)
{
    private const int LookbackDays = 14;
    private const int ActivityDays = 7;
    private const int MaximumThreadsPerAccount = 75;
    private const int MaximumConcurrentThreadRequests = 8;

    public async Task<ControlCenterSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var accounts = await database.MailAccounts
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive && x.Provider == MailProviderType.Gmail)
            .OrderBy(x => x.DisplayName)
            .ToArrayAsync(cancellationToken);
        var states = await database.ControlCenterStates
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToArrayAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var results = new List<AccountResult>(accounts.Length);
        foreach (var account in accounts)
            results.Add(await LoadAccountSafelyAsync(account, now, cancellationToken));

        var available = results.Where(x => x.IsAvailable).ToArray();
        var stateLookup = states.ToDictionary(x => (x.AccountId, x.ConversationId));
        var pending = available
            .SelectMany(x => x.PendingItems)
            .Where(item => !IsSuppressed(item, stateLookup, now))
            .OrderBy(x => x.Since)
            .ToArray();

        var activity = Enumerable.Range(0, ActivityDays)
            .Select(offset => now.UtcDateTime.Date.AddDays(-(ActivityDays - 1 - offset)))
            .Select(day => new ControlCenterDay(
                day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                available.Sum(x => x.Activity.GetValueOrDefault(day)?.Received ?? 0),
                available.Sum(x => x.Activity.GetValueOrDefault(day)?.Sent ?? 0)))
            .ToArray();

        var priorityItems = pending.Take(6).Select(ToPendingItem).ToArray();
        var pendingItems = pending.Select(ToPendingItem).ToArray();
        var accountSummaries = results.Select(x => new ControlCenterAccountSummary(
            x.AccountId,
            x.AccountName,
            x.AccountColor,
            pending.Count(item => item.AccountId == x.AccountId && item.Direction == "received"),
            pending.Count(item => item.AccountId == x.AccountId && item.Direction == "sent"),
            x.Unread,
            x.IsAvailable)).ToArray();

        return new ControlCenterSnapshot(
            pending.Count(x => x.Direction == "received"),
            pending.Count(x => x.Direction == "sent"),
            available.Sum(x => x.Unread),
            pending.Count(x => now - x.Since >= TimeSpan.FromHours(48)),
            activity,
            priorityItems,
            pendingItems,
            accountSummaries,
            results.Count(x => !x.IsAvailable),
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
        }
        else
        {
            state.Status = "snoozed";
            state.SnoozedUntil = now.AddHours(Math.Clamp(snoozeHours ?? 24, 1, 24 * 30));
        }

        await database.SaveChangesAsync(cancellationToken);
        return true;
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

    private async Task<AccountResult> LoadAccountSafelyAsync(MailAccountEntity account, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            return await LoadAccountAsync(account, now, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
        {
            return AccountResult.Unavailable(account);
        }
    }

    private async Task<AccountResult> LoadAccountAsync(MailAccountEntity account, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(account.Id, cancellationToken);
        var unread = await GetUnreadCountAsync(client, cancellationToken);
        var threadIds = await GetRecentThreadIdsAsync(client, cancellationToken);

        using var gate = new SemaphoreSlim(MaximumConcurrentThreadRequests);
        var threadTasks = threadIds.Select(async threadId =>
        {
            await gate.WaitAsync(cancellationToken);
            try { return await GetThreadAsync(client, threadId, cancellationToken); }
            finally { gate.Release(); }
        });
        var threads = (await Task.WhenAll(threadTasks)).Where(x => x is not null).Select(x => x!).ToArray();

        var activityStart = now.UtcDateTime.Date.AddDays(-(ActivityDays - 1));
        var activity = Enumerable.Range(0, ActivityDays)
            .Select(offset => activityStart.AddDays(offset))
            .ToDictionary(day => day, _ => new ActivityCount());
        var pending = new List<PendingRaw>();
        var lookbackStart = now.AddDays(-LookbackDays);

        foreach (var thread in threads)
        {
            foreach (var message in thread.Messages)
            {
                var day = message.ReceivedAt.UtcDateTime.Date;
                if (!activity.TryGetValue(day, out var count)) continue;
                if (message.Labels.Contains("SENT")) count.Sent++;
                else if (message.Labels.Contains("INBOX")) count.Received++;
            }

            var latest = thread.Messages.OrderByDescending(x => x.ReceivedAt).FirstOrDefault();
            if (latest is null || latest.ReceivedAt < lookbackStart) continue;

            if (latest.Labels.Contains("SENT"))
            {
                pending.Add(new PendingRaw(
                    account.Id,
                    account.DisplayName,
                    account.Color,
                    latest.Id,
                    thread.Id,
                    "sent",
                    DisplayCounterpart(latest.To),
                    DisplaySubject(latest.Subject),
                    latest.ReceivedAt,
                    true));
                continue;
            }

            if (latest.Labels.Contains("INBOX") && !IsLikelyAutomated(latest))
            {
                pending.Add(new PendingRaw(
                    account.Id,
                    account.DisplayName,
                    account.Color,
                    latest.Id,
                    thread.Id,
                    "received",
                    DisplayCounterpart(latest.From),
                    DisplaySubject(latest.Subject),
                    latest.ReceivedAt,
                    !latest.Labels.Contains("UNREAD")));
            }
        }

        return new AccountResult(account.Id, account.DisplayName, account.Color, unread, pending, activity, true);
    }

    private async Task<HttpClient> CreateClientAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var credential = await database.OAuthCredentials
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.MailAccountId == accountId, cancellationToken)
            ?? throw new InvalidOperationException("No existe una credencial OAuth para esta cuenta.");

        var settings = options.Value;
        var refreshToken = tokenProtector.Unprotect(credential.EncryptedRefreshToken);
        var tokenClient = httpClientFactory.CreateClient();
        using var response = await tokenClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        }), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));

        var client = httpClientFactory.CreateClient("Gmail");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", document.RootElement.GetProperty("access_token").GetString());
        return client;
    }

    private static async Task<int> GetUnreadCountAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("users/me/labels/INBOX", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        return document.RootElement.TryGetProperty("messagesUnread", out var unread) && unread.TryGetInt32(out var count) ? count : 0;
    }

    private static async Task<IReadOnlyCollection<string>> GetRecentThreadIdsAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString($"newer_than:{LookbackDays}d {{in:inbox in:sent}}");
        using var response = await client.GetAsync($"users/me/threads?maxResults={MaximumThreadsPerAccount}&q={query}", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("threads", out var threads)) return [];
        return threads.EnumerateArray()
            .Select(x => x.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToArray();
    }

    private static async Task<ThreadData?> GetThreadAsync(HttpClient client, string threadId, CancellationToken cancellationToken)
    {
        var url = $"users/me/threads/{Uri.EscapeDataString(threadId)}?format=metadata" +
                  "&metadataHeaders=From&metadataHeaders=To&metadataHeaders=Subject" +
                  "&metadataHeaders=Auto-Submitted&metadataHeaders=Precedence&metadataHeaders=List-Unsubscribe";
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("messages", out var messages)) return null;
        return new ThreadData(threadId, messages.EnumerateArray().Select(ParseMessage).Where(x => x is not null).Select(x => x!).ToArray());
    }

    private static ThreadMessageData? ParseMessage(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var idElement) || string.IsNullOrWhiteSpace(idElement.GetString())) return null;
        var headers = Headers(root);
        var labels = root.TryGetProperty("labelIds", out var labelIds)
            ? labelIds.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var receivedAt = root.TryGetProperty("internalDate", out var timestamp) && long.TryParse(timestamp.GetString(), out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : DateTimeOffset.UtcNow;

        return new ThreadMessageData(
            idElement.GetString()!,
            Header(headers, "From"),
            Header(headers, "To"),
            Header(headers, "Subject"),
            Header(headers, "Auto-Submitted"),
            Header(headers, "Precedence"),
            Header(headers, "List-Unsubscribe"),
            receivedAt,
            labels);
    }

    private static Dictionary<string, string> Headers(JsonElement root)
    {
        if (!root.TryGetProperty("payload", out var payload) || !payload.TryGetProperty("headers", out var headers))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return headers.EnumerateArray()
            .Where(x => x.TryGetProperty("name", out _) && x.TryGetProperty("value", out _))
            .GroupBy(x => x.GetProperty("name").GetString()!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().GetProperty("value").GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    private static string Header(IReadOnlyDictionary<string, string> headers, string name) => headers.TryGetValue(name, out var value) ? value : string.Empty;

    private static bool IsLikelyAutomated(ThreadMessageData message)
    {
        var from = message.From.ToLowerInvariant();
        if (from.Contains("no-reply") || from.Contains("noreply") || from.Contains("do-not-reply") || from.Contains("donotreply") || from.Contains("mailer-daemon")) return true;
        if (!string.IsNullOrWhiteSpace(message.ListUnsubscribe)) return true;
        if (!string.IsNullOrWhiteSpace(message.AutoSubmitted) && !string.Equals(message.AutoSubmitted, "no", StringComparison.OrdinalIgnoreCase)) return true;
        return message.Precedence.Equals("bulk", StringComparison.OrdinalIgnoreCase) ||
               message.Precedence.Equals("list", StringComparison.OrdinalIgnoreCase) ||
               message.Precedence.Equals("junk", StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplayCounterpart(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Sin destinatario";
        var first = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? value;
        return first.Length <= 90 ? first : first[..87] + "…";
    }

    private static string DisplaySubject(string value)
    {
        var subject = string.IsNullOrWhiteSpace(value) ? "(sin asunto)" : value.Trim();
        return subject.Length <= 120 ? subject : subject[..117] + "…";
    }

    private sealed record ThreadData(string Id, IReadOnlyCollection<ThreadMessageData> Messages);

    private sealed record ThreadMessageData(
        string Id,
        string From,
        string To,
        string Subject,
        string AutoSubmitted,
        string Precedence,
        string ListUnsubscribe,
        DateTimeOffset ReceivedAt,
        HashSet<string> Labels);

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

    private sealed record AccountResult(
        Guid AccountId,
        string AccountName,
        string AccountColor,
        int Unread,
        IReadOnlyCollection<PendingRaw> PendingItems,
        Dictionary<DateTime, ActivityCount> Activity,
        bool IsAvailable)
    {
        public static AccountResult Unavailable(MailAccountEntity account) => new(
            account.Id,
            account.DisplayName,
            account.Color,
            0,
            [],
            [],
            false);
    }

    private sealed class ActivityCount
    {
        public int Received { get; set; }
        public int Sent { get; set; }
    }
}
