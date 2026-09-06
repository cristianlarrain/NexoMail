using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Google;

public sealed class GmailControlCenterActivityService(
    IHttpClientFactory httpClientFactory,
    NexoMailDbContext database,
    ITokenProtector tokenProtector,
    IOptions<GmailOptions> options,
    IUserContext userContext)
{
    private const int MaximumConcurrentRequests = 8;

    public async Task<ControlCenterActivitySnapshot> GetActivityAsync(Guid? accountId, int? requestedDays, int? requestedOffsetDays, CancellationToken cancellationToken)
    {
        var days = requestedDays is 14 or 30 ? requestedDays.Value : 7;
        var offsetDays = Math.Clamp(requestedOffsetDays ?? 0, 0, 365);
        var now = DateTimeOffset.UtcNow;
        var endDay = now.UtcDateTime.Date.AddDays(-offsetDays);
        var startDay = endDay.AddDays(-(days - 1));
        var userId = userContext.UserId;

        var accountQuery = database.MailAccounts
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive && x.Provider == MailProviderType.Gmail);
        if (accountId.HasValue) accountQuery = accountQuery.Where(x => x.Id == accountId.Value);
        var accounts = await accountQuery.OrderBy(x => x.DisplayName).ToArrayAsync(cancellationToken);

        var totals = Enumerable.Range(0, days)
            .Select(offset => startDay.AddDays(offset))
            .ToDictionary(day => day, _ => new ActivityCount());
        var unavailableAccounts = 0;

        foreach (var account in accounts)
        {
            try
            {
                var client = await CreateClientAsync(account.Id, cancellationToken);
                var accountActivity = await LoadActivityAsync(client, startDay, days, cancellationToken);
                foreach (var (day, count) in accountActivity)
                {
                    totals[day].Received += count.Received;
                    totals[day].Sent += count.Sent;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException or OperationCanceledException)
            {
                unavailableAccounts++;
            }
        }

        var activity = totals
            .OrderBy(x => x.Key)
            .Select(x => new ControlCenterDay(
                x.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                x.Value.Received,
                x.Value.Sent))
            .ToArray();

        return new ControlCenterActivitySnapshot(
            days,
            offsetDays,
            startDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            endDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            activity,
            unavailableAccounts,
            now);
    }

    private async Task<Dictionary<DateTime, ActivityCount>> LoadActivityAsync(HttpClient client, DateTime startDay, int days, CancellationToken cancellationToken)
    {
        var result = Enumerable.Range(0, days)
            .Select(offset => startDay.AddDays(offset))
            .ToDictionary(day => day, _ => new ActivityCount());
        using var gate = new SemaphoreSlim(MaximumConcurrentRequests);

        var tasks = result.Keys.Select(async day =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var start = new DateTimeOffset(DateTime.SpecifyKind(day, DateTimeKind.Utc)).ToUnixTimeSeconds();
                var end = new DateTimeOffset(DateTime.SpecifyKind(day.AddDays(1), DateTimeKind.Utc)).ToUnixTimeSeconds();
                var receivedTask = CountMessagesAsync(client, "INBOX", start, end, cancellationToken);
                var sentTask = CountMessagesAsync(client, "SENT", start, end, cancellationToken);
                await Task.WhenAll(receivedTask, sentTask);
                return (day, received: receivedTask.Result, sent: sentTask.Result);
            }
            finally
            {
                gate.Release();
            }
        });

        foreach (var item in await Task.WhenAll(tasks))
        {
            result[item.day].Received = item.received;
            result[item.day].Sent = item.sent;
        }

        return result;
    }

    private static async Task<int> CountMessagesAsync(HttpClient client, string labelId, long startEpoch, long endEpoch, CancellationToken cancellationToken)
    {
        var total = 0;
        string? pageToken = null;
        do
        {
            var query = Uri.EscapeDataString($"after:{startEpoch - 1} before:{endEpoch}");
            var url = $"users/me/messages?maxResults=500&labelIds={labelId}&q={query}";
            if (!string.IsNullOrWhiteSpace(pageToken)) url += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            if (document.RootElement.TryGetProperty("messages", out var messages)) total += messages.GetArrayLength();
            pageToken = document.RootElement.TryGetProperty("nextPageToken", out var token) ? token.GetString() : null;
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        return total;
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

    private sealed class ActivityCount
    {
        public int Received { get; set; }
        public int Sent { get; set; }
    }
}