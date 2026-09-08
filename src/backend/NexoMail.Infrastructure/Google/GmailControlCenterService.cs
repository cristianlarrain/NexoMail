using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
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
    private const int MaximumConcurrentAccounts = 2;
    private const string ManualTrackingPrefix = "manual:";
    private static readonly ConcurrentDictionary<Guid, CachedAccessToken> AccessTokens = new();
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> TokenGates = new();

    public async Task<ControlCenterSnapshot> GetSnapshotAsync(Guid? accountId, CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var accountQuery = database.MailAccounts
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive && x.Provider == MailProviderType.Gmail);
        if (accountId.HasValue) accountQuery = accountQuery.Where(x => x.Id == accountId.Value);
        var accounts = await accountQuery.OrderBy(x => x.DisplayName).ToArrayAsync(cancellationToken);

        var stateQuery = database.ControlCenterStates.AsNoTracking().Where(x => x.UserId == userId);
        if (accountId.HasValue) stateQuery = stateQuery.Where(x => x.AccountId == accountId.Value);
        var states = await stateQuery.ToArrayAsync(cancellationToken);

        var accountIds = accounts.Select(x => x.Id).ToArray();
        var credentials = accountIds.Length == 0
            ? new Dictionary<Guid, CredentialSnapshot>()
            : await database.OAuthCredentials
                .AsNoTracking()
                .Where(x => accountIds.Contains(x.MailAccountId))
                .ToDictionaryAsync(
                    x => x.MailAccountId,
                    x => new CredentialSnapshot(x.EncryptedRefreshToken, x.UpdatedAt),
                    cancellationToken);

        var now = DateTimeOffset.UtcNow;
        using var accountGate = new SemaphoreSlim(MaximumConcurrentAccounts);
        var resultTasks = accounts.Select(async account =>
        {
            await accountGate.WaitAsync(cancellationToken);
            try
            {
                return credentials.TryGetValue(account.Id, out var credential)
                    ? await LoadAccountSafelyAsync(account, credential, now, cancellationToken)
                    : AccountResult.Unavailable(account);
            }
            finally
            {
                accountGate.Release();
            }
        });
        var results = await Task.WhenAll(resultTasks);

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

    private async Task<AccountResult> LoadAccountSafelyAsync(MailAccountEntity account, CredentialSnapshot credential, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            return await LoadAccountAsync(account, credential, now, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException or OperationCanceledException)
        {
            return AccountResult.Unavailable(account);
        }
    }

    private async Task<AccountResult> LoadAccountAsync(MailAccountEntity account, CredentialSnapshot credential, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(account.Id, credential, cancellationToken);
        var unreadTask = GetUnreadCountAsync(client, cancellationToken);
        var threadIdsTask = GetRecentThreadIdsAsync(client, cancellationToken);
        await Task.WhenAll(unreadTask, threadIdsTask);
        var unread = await unreadTask;
        var threadIds = await threadIdsTask;

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

            if (latest.Labels.Contains("INBOX") && !IsNonActionableReceived(latest))
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

    private async Task<HttpClient> CreateClientAsync(Guid accountId, CredentialSnapshot credential, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!AccessTokens.TryGetValue(accountId, out var cached) || cached.CredentialUpdatedAt != credential.UpdatedAt || cached.ExpiresAt <= now.AddMinutes(2))
        {
            var gate = TokenGates.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                now = DateTimeOffset.UtcNow;
                if (!AccessTokens.TryGetValue(accountId, out cached) || cached.CredentialUpdatedAt != credential.UpdatedAt || cached.ExpiresAt <= now.AddMinutes(2))
                {
                    var refreshToken = tokenProtector.Unprotect(credential.EncryptedRefreshToken);
                    var tokenClient = httpClientFactory.CreateClient();
                    using var response = await tokenClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = options.Value.ClientId,
                        ["client_secret"] = options.Value.ClientSecret,
                        ["refresh_token"] = refreshToken,
                        ["grant_type"] = "refresh_token"
                    }), cancellationToken);
                    response.EnsureSuccessStatusCode();
                    using var tokenDocument = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
                    var accessToken = tokenDocument.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Google no entregó un token de acceso válido.");
                    var expiresIn = tokenDocument.RootElement.TryGetProperty("expires_in", out var expiresElement) && expiresElement.TryGetInt32(out var seconds) ? seconds : 3600;
                    cached = new CachedAccessToken(accessToken, now.AddSeconds(Math.Max(300, expiresIn)), credential.UpdatedAt);
                    AccessTokens[accountId] = cached;
                }
            }
            finally
            {
                gate.Release();
            }
        }

        var client = httpClientFactory.CreateClient("Gmail");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cached.AccessToken);
        return client;
    }

    private async Task<int> GetUnreadCountAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("users/me/labels/INBOX", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        return document.RootElement.TryGetProperty("messagesUnread", out var unread) && unread.TryGetInt32(out var count) ? count : 0;
    }

    private async Task<IReadOnlyCollection<string>> GetRecentThreadIdsAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-LookbackDays).ToUnixTimeSeconds();
        var query = Uri.EscapeDataString($"after:{cutoff} -label:drafts -label:spam -label:trash");
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

    private async Task<ThreadSnapshot?> GetThreadAsync(HttpClient client, string threadId, CancellationToken cancellationToken)
    {
        const string fields = "id,messages(id,labelIds,internalDate,snippet,payload(headers))";
        using var response = await client.GetAsync($"users/me/threads/{Uri.EscapeDataString(threadId)}?format=metadata&metadataHeaders=From&metadataHeaders=To&metadataHeaders=Subject&fields={Uri.EscapeDataString(fields)}", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("messages", out var messages)) return null;
        var values = messages.EnumerateArray().Select(ParseThreadMessage).Where(x => x is not null).Select(x => x!).OrderBy(x => x.ReceivedAt).ToArray();
        return values.Length == 0 ? null : new ThreadSnapshot(threadId, values);
    }

    private static ThreadMessage? ParseThreadMessage(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var idElement) || string.IsNullOrWhiteSpace(idElement.GetString())) return null;
        var labels = root.TryGetProperty("labelIds", out var labelElement)
            ? labelElement.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var receivedAt = root.TryGetProperty("internalDate", out var dateElement) && long.TryParse(dateElement.GetString(), out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : DateTimeOffset.UtcNow;
        var payload = root.TryGetProperty("payload", out var payloadElement) ? payloadElement : default;
        var headers = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("headers", out var headerElement)
            ? headerElement.EnumerateArray()
                .Where(x => x.TryGetProperty("name", out _) && x.TryGetProperty("value", out _))
                .ToDictionary(
                    x => x.GetProperty("name").GetString() ?? string.Empty,
                    x => x.GetProperty("value").GetString() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return new ThreadMessage(
            idElement.GetString()!,
            labels,
            receivedAt,
            headers.GetValueOrDefault("From") ?? string.Empty,
            headers.GetValueOrDefault("To") ?? string.Empty,
            headers.GetValueOrDefault("Subject") ?? string.Empty,
            root.TryGetProperty("snippet", out var snippet) ? snippet.GetString() ?? string.Empty : string.Empty);
    }

    private static bool IsNonActionableReceived(ThreadMessage message)
    {
        var sender = message.From.ToLowerInvariant();
        var subject = message.Subject.ToLowerInvariant();
        var snippet = message.Snippet.ToLowerInvariant();
        var combined = $"{subject} {snippet}";
        return sender.Contains("no-reply")
            || sender.Contains("noreply")
            || sender.Contains("mailer-daemon")
            || sender.Contains("notifications@")
            || combined.Contains("unsubscribe")
            || combined.Contains("desuscrib")
            || combined.Contains("notificación automática")
            || combined.Contains("notification")
            || combined.Contains("código de verificación")
            || combined.Contains("verification code")
            || combined.Contains("recibo de compra")
            || combined.Contains("receipt")
            || combined.Contains("promoción")
            || combined.Contains("promotion");
    }

    private static string DisplayCounterpart(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Contacto";
        var match = System.Text.RegularExpressions.Regex.Match(raw, "^(?<name>.*?)\\s*<(?<email>[^>]+)>$");
        if (match.Success)
        {
            var name = match.Groups["name"].Value.Trim().Trim('"');
            return string.IsNullOrWhiteSpace(name) ? match.Groups["email"].Value.Trim() : name;
        }
        return raw.Trim();
    }

    private static string DisplaySubject(string raw) => string.IsNullOrWhiteSpace(raw) ? "(sin asunto)" : raw.Trim();

    private sealed record CachedAccessToken(string AccessToken, DateTimeOffset ExpiresAt, DateTimeOffset CredentialUpdatedAt);
    private sealed record CredentialSnapshot(string EncryptedRefreshToken, DateTimeOffset UpdatedAt);
    private sealed class ActivityCount { public int Received { get; set; } public int Sent { get; set; } }
    private sealed record ThreadSnapshot(string Id, IReadOnlyCollection<ThreadMessage> Messages);
    private sealed record ThreadMessage(string Id, HashSet<string> Labels, DateTimeOffset ReceivedAt, string From, string To, string Subject, string Snippet);
    private sealed record PendingRaw(Guid AccountId, string AccountName, string AccountColor, string MessageId, string ConversationId, string Direction, string Counterpart, string Subject, DateTimeOffset Since, bool IsRead);
    private sealed record AccountResult(Guid AccountId, string AccountName, string AccountColor, int Unread, IReadOnlyCollection<PendingRaw> PendingItems, IReadOnlyDictionary<DateTime, ActivityCount> Activity, bool IsAvailable)
    {
        public static AccountResult Unavailable(MailAccountEntity account) => new(account.Id, account.DisplayName, account.Color, 0, [], new Dictionary<DateTime, ActivityCount>(), false);
    }
}
