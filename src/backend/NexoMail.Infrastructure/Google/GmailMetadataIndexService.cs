using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Google;

/// <summary>
/// Builds a lightweight local index from Gmail metadata only. Message bodies and attachment bytes are never persisted.
/// </summary>
public sealed class GmailMetadataIndexService(
    IHttpClientFactory httpClientFactory,
    NexoMailDbContext database,
    ITokenProtector tokenProtector,
    IOptions<GmailOptions> options,
    IUserContext userContext)
{
    private const int MaximumConcurrentRequests = 8;
    private const int DefaultSyncLimitPerAccount = 80;
    private const int MaximumSyncLimitPerAccount = 300;
    private const int DefaultWindowDays = 90;
    private const int RefreshNewestCount = 24;

    public async Task<MailMetadataSyncResult> SyncAsync(int? requestedDays, int? requestedLimitPerAccount, CancellationToken cancellationToken)
    {
        var days = Math.Clamp(requestedDays ?? DefaultWindowDays, 7, 365);
        var limitPerAccount = Math.Clamp(requestedLimitPerAccount ?? DefaultSyncLimitPerAccount, 25, MaximumSyncLimitPerAccount);
        var userId = userContext.UserId;
        var accounts = await database.MailAccounts
            .Where(x => x.UserId == userId && x.IsActive && x.Provider == MailProviderType.Gmail)
            .OrderBy(x => x.DisplayName)
            .ToArrayAsync(cancellationToken);

        var totalMessages = 0;
        var totalAttachments = 0;
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddDays(-days);

        foreach (var account in accounts)
        {
            var client = await CreateClientAsync(account.Id, cancellationToken);
            var existingCount = await database.MailMessageIndex
                .AsNoTracking()
                .CountAsync(x => x.UserId == userId && x.AccountId == account.Id && x.OccurredAt >= cutoff, cancellationToken);

            List<string> ids;
            if (existingCount == 0)
            {
                ids = await ListMessageIdsAsync(client, days, limitPerAccount, null, cancellationToken);
            }
            else
            {
                var newestLimit = Math.Min(RefreshNewestCount, limitPerAccount);
                var newest = await ListMessageIdsAsync(client, days, newestLimit, null, cancellationToken);
                var oldest = await database.MailMessageIndex
                    .AsNoTracking()
                    .Where(x => x.UserId == userId && x.AccountId == account.Id && x.OccurredAt >= cutoff)
                    .MinAsync(x => (DateTimeOffset?)x.OccurredAt, cancellationToken);
                var backfillLimit = Math.Max(0, limitPerAccount - newest.Count);
                var older = oldest.HasValue && oldest.Value > cutoff && backfillLimit > 0
                    ? await ListMessageIdsAsync(client, days, backfillLimit, oldest.Value.ToUnixTimeSeconds(), cancellationToken)
                    : [];
                ids = newest.Concat(older).Distinct(StringComparer.Ordinal).Take(limitPerAccount).ToList();
            }

            var indexed = await LoadMetadataAsync(client, ids, cancellationToken);
            totalMessages += indexed.Count;
            totalAttachments += indexed.Sum(x => x.Attachments.Count);

            if (indexed.Count > 0)
                await UpsertAccountIndexAsync(account, indexed, now, days, cancellationToken);
            else
                await UpsertStateAsync(account, now, days, existingCount, cancellationToken);
        }

        return new MailMetadataSyncResult(accounts.Length, totalMessages, totalAttachments, now);
    }

    public async Task<ContactAnalyticsSnapshot> GetContactsAsync(int? requestedDays, CancellationToken cancellationToken)
    {
        var days = requestedDays is 90 ? 90 : 30;
        var userId = userContext.UserId;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        var messages = await database.MailMessageIndex
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.OccurredAt >= cutoff)
            .OrderBy(x => x.OccurredAt)
            .ToArrayAsync(cancellationToken);

        var accounts = await database.MailAccounts
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var ownAddresses = accounts.Values.Select(x => NormalizeEmail(x.EmailAddress)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contacts = new Dictionary<string, ContactAccumulator>(StringComparer.OrdinalIgnoreCase);
        var timelines = new Dictionary<string, List<TimelineEvent>>(StringComparer.Ordinal);

        foreach (var message in messages.Where(x => x.Direction == "sent"))
        {
            foreach (var recipient in DeserializeAddresses(message.ToAddresses))
            {
                var email = NormalizeEmail(recipient.Address);
                if (!email.Contains('@') || ownAddresses.Contains(email) || IsNonPersonalAddress(email)) continue;
                if (!contacts.TryGetValue(email, out var contact))
                {
                    contact = new ContactAccumulator(email, ContactName(recipient.Name, email));
                    contacts[email] = contact;
                }

                contact.Sent++;
                contact.LastInteraction = Max(contact.LastInteraction, message.OccurredAt);
                if (accounts.TryGetValue(message.AccountId, out var account)) contact.Accounts.Add(account.DisplayName);
                contact.AddSubject(message.Subject, message.OccurredAt);
                Timeline(timelines, message.ThreadId, email).Add(new TimelineEvent(message.OccurredAt, true));
            }
        }

        foreach (var message in messages.Where(x => x.Direction == "received"))
        {
            var email = NormalizeEmail(message.FromAddress);
            if (!contacts.TryGetValue(email, out var contact) || IsNonPersonalAddress(email)) continue;
            contact.Received++;
            contact.LastInteraction = Max(contact.LastInteraction, message.OccurredAt);
            if (string.IsNullOrWhiteSpace(contact.Name) || contact.Name == email.Split('@')[0]) contact.Name = ContactName(message.FromName, email);
            if (accounts.TryGetValue(message.AccountId, out var account)) contact.Accounts.Add(account.DisplayName);
            contact.AddSubject(message.Subject, message.OccurredAt);
            Timeline(timelines, message.ThreadId, email).Add(new TimelineEvent(message.OccurredAt, false));
        }

        foreach (var (key, events) in timelines)
        {
            var separator = key.LastIndexOf('|');
            if (separator < 0) continue;
            var email = key[(separator + 1)..];
            if (!contacts.TryGetValue(email, out var contact)) continue;
            DateTimeOffset? waitingSince = null;
            foreach (var item in events.OrderBy(x => x.At))
            {
                if (item.Sent)
                {
                    waitingSince = item.At;
                    continue;
                }
                if (waitingSince.HasValue && item.At >= waitingSince.Value)
                {
                    contact.Replies++;
                    contact.ResponseMinutes.Add(Math.Max(0, (int)Math.Round((item.At - waitingSince.Value).TotalMinutes)));
                    waitingSince = null;
                }
            }
            if (waitingSince.HasValue) contact.Awaiting++;
        }

        var items = contacts.Values
            .Where(x => x.Sent > 0)
            .OrderByDescending(x => x.Sent)
            .ThenByDescending(x => x.Received)
            .Select(x => new ContactAnalyticsItem(
                x.Email,
                x.Name,
                x.Accounts.OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase).ToArray(),
                x.Sent,
                x.Received,
                x.Replies,
                x.Awaiting,
                Average(x.ResponseMinutes),
                x.LastInteraction ?? DateTimeOffset.UtcNow,
                x.Subjects.Values.OrderByDescending(v => v.Count).ThenByDescending(v => v.LastAt).Take(3).Select(v => v.Subject).ToArray()))
            .ToArray();

        var allResponseMinutes = contacts.Values.SelectMany(x => x.ResponseMinutes).ToArray();
        var states = await database.MailIndexStates.AsNoTracking().Where(x => x.UserId == userId).ToArrayAsync(cancellationToken);
        return new ContactAnalyticsSnapshot(
            days,
            items,
            items.Sum(x => x.Sent),
            items.Sum(x => x.Received),
            items.Sum(x => x.Replies),
            items.Sum(x => x.Awaiting),
            Average(allResponseMinutes),
            messages.Length,
            states.Length == 0 ? null : states.Max(x => x.LastIndexedAt));
    }

    public async Task<DocumentIndexSnapshot> GetDocumentsAsync(string? search, string? type, int? requestedTake, int? requestedSkip, CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var take = Math.Clamp(requestedTake ?? 50, 10, 200);
        var skip = Math.Max(0, requestedSkip ?? 0);
        var term = search?.Trim().ToLowerInvariant() ?? string.Empty;
        var typeFilter = type?.Trim() ?? string.Empty;

        var messages = await database.MailMessageIndex.AsNoTracking()
            .Where(x => x.UserId == userId && x.Direction == "received" && x.HasAttachments)
            .ToDictionaryAsync(x => $"{x.AccountId:N}|{x.ProviderMessageId}", cancellationToken);
        var indexedMessageCount = await database.MailMessageIndex.AsNoTracking().CountAsync(x => x.UserId == userId, cancellationToken);
        var accounts = await database.MailAccounts.AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var attachments = await database.MailAttachmentIndex.AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToArrayAsync(cancellationToken);

        var rows = attachments
            .Select(attachment =>
            {
                messages.TryGetValue($"{attachment.AccountId:N}|{attachment.ProviderMessageId}", out var message);
                accounts.TryGetValue(attachment.AccountId, out var account);
                return (attachment, message, account, documentType: DocumentType(attachment.FileName, attachment.ContentType));
            })
            .Where(x => x.message is not null && IsUsefulDocument(x.attachment.FileName))
            .Where(x => string.IsNullOrWhiteSpace(typeFilter) || string.Equals(typeFilter, "all", StringComparison.OrdinalIgnoreCase) || string.Equals(x.documentType, typeFilter, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(term) || new[] { x.attachment.FileName, x.documentType, x.message!.FromName, x.message.FromAddress, x.message.Subject, x.message.Snippet }
                .Any(value => (value ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.message!.OccurredAt)
            .ToArray();

        var page = rows.Skip(skip).Take(take).Select(x => new DocumentIndexItem(
            x.attachment.AccountId,
            x.account?.DisplayName ?? "Cuenta",
            x.attachment.ProviderMessageId,
            x.attachment.AttachmentId,
            x.attachment.FileName,
            x.attachment.ContentType,
            x.attachment.Size,
            x.documentType,
            x.message!.OccurredAt,
            string.IsNullOrWhiteSpace(x.message.FromName) ? x.message.FromAddress : x.message.FromName,
            x.message.FromAddress,
            x.message.Subject,
            string.IsNullOrWhiteSpace(x.message.Snippet) ? x.message.Subject : x.message.Snippet)).ToArray();

        var states = await database.MailIndexStates.AsNoTracking().Where(x => x.UserId == userId).ToArrayAsync(cancellationToken);
        return new DocumentIndexSnapshot(page, rows.Length, skip + page.Length < rows.Length, indexedMessageCount, states.Length == 0 ? null : states.Max(x => x.LastIndexedAt));
    }

    private async Task UpsertAccountIndexAsync(MailAccountEntity account, IReadOnlyCollection<IndexedMessage> indexed, DateTimeOffset now, int days, CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var ids = indexed.Select(x => x.MessageId).ToArray();
        var existing = await database.MailMessageIndex
            .Where(x => x.UserId == userId && x.AccountId == account.Id && ids.Contains(x.ProviderMessageId))
            .ToDictionaryAsync(x => x.ProviderMessageId, cancellationToken);

        var oldAttachments = await database.MailAttachmentIndex
            .Where(x => x.UserId == userId && x.AccountId == account.Id && ids.Contains(x.ProviderMessageId))
            .ToArrayAsync(cancellationToken);
        if (oldAttachments.Length > 0) database.MailAttachmentIndex.RemoveRange(oldAttachments);

        foreach (var message in indexed)
        {
            if (!existing.TryGetValue(message.MessageId, out var entity))
            {
                entity = new MailMessageIndexEntity { Id = Guid.NewGuid(), UserId = userId, AccountId = account.Id, ProviderMessageId = message.MessageId };
                database.MailMessageIndex.Add(entity);
            }
            entity.ThreadId = message.ThreadId;
            entity.Direction = message.Direction;
            entity.FromName = message.From.Name;
            entity.FromAddress = message.From.Address;
            entity.ToAddresses = SerializeAddresses(message.To);
            entity.Subject = message.Subject;
            entity.Snippet = message.Snippet;
            entity.OccurredAt = message.OccurredAt;
            entity.HasAttachments = message.Attachments.Count > 0;
            entity.IndexedAt = now;

            foreach (var attachment in message.Attachments)
            {
                database.MailAttachmentIndex.Add(new MailAttachmentIndexEntity
                {
                    Id = Guid.NewGuid(), UserId = userId, AccountId = account.Id, ProviderMessageId = message.MessageId,
                    AttachmentId = attachment.AttachmentId, FileName = attachment.FileName, ContentType = attachment.ContentType,
                    Size = attachment.Size, IndexedAt = now
                });
            }
        }

        await database.SaveChangesAsync(cancellationToken);
        var totalCount = await database.MailMessageIndex.AsNoTracking().CountAsync(x => x.UserId == userId && x.AccountId == account.Id, cancellationToken);
        await UpsertStateAsync(account, now, days, totalCount, cancellationToken);
    }

    private async Task UpsertStateAsync(MailAccountEntity account, DateTimeOffset now, int days, int count, CancellationToken cancellationToken)
    {
        var state = await database.MailIndexStates.SingleOrDefaultAsync(x => x.AccountId == account.Id, cancellationToken);
        if (state is null)
        {
            state = new MailIndexStateEntity { AccountId = account.Id, UserId = userContext.UserId };
            database.MailIndexStates.Add(state);
        }
        state.LastIndexedAt = now;
        state.WindowDays = days;
        state.IndexedMessageCount = count;
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<string>> ListMessageIdsAsync(HttpClient client, int days, int limit, long? beforeEpoch, CancellationToken cancellationToken)
    {
        var ids = new List<string>(limit);
        string? pageToken = null;
        do
        {
            var remaining = Math.Min(100, limit - ids.Count);
            var before = beforeEpoch.HasValue ? $" before:{beforeEpoch.Value}" : string.Empty;
            var query = Uri.EscapeDataString($"newer_than:{days}d{before} -label:drafts -label:spam -label:trash");
            var url = $"users/me/messages?maxResults={remaining}&q={query}";
            if (!string.IsNullOrWhiteSpace(pageToken)) url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            if (document.RootElement.TryGetProperty("messages", out var messages))
                foreach (var item in messages.EnumerateArray())
                    if (item.TryGetProperty("id", out var id) && !string.IsNullOrWhiteSpace(id.GetString())) ids.Add(id.GetString()!);
            pageToken = document.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        }
        while (ids.Count < limit && !string.IsNullOrWhiteSpace(pageToken));
        return ids;
    }

    private async Task<IReadOnlyCollection<IndexedMessage>> LoadMetadataAsync(HttpClient client, IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(MaximumConcurrentRequests);
        var tasks = ids.Select(async id =>
        {
            await gate.WaitAsync(cancellationToken);
            try { return await LoadMessageMetadataAsync(client, id, cancellationToken); }
            catch (HttpRequestException) { return null; }
            catch (JsonException) { return null; }
            finally { gate.Release(); }
        });
        return (await Task.WhenAll(tasks)).Where(x => x is not null).Cast<IndexedMessage>().ToArray();
    }

    private static async Task<IndexedMessage?> LoadMessageMetadataAsync(HttpClient client, string id, CancellationToken cancellationToken)
    {
        const string fields = "id,threadId,labelIds,internalDate,snippet,payload(headers,filename,mimeType,body/attachmentId,body/size,parts/filename,parts/mimeType,parts/body/attachmentId,parts/body/size,parts/parts/filename,parts/parts/mimeType,parts/parts/body/attachmentId,parts/parts/body/size)";
        using var response = await client.GetAsync($"users/me/messages/{Uri.EscapeDataString(id)}?format=full&fields={Uri.EscapeDataString(fields)}", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var root = document.RootElement;
        if (!root.TryGetProperty("payload", out var payload)) return null;
        var headers = Headers(payload);
        var labels = root.TryGetProperty("labelIds", out var labelIds) ? labelIds.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        var occurredAt = root.TryGetProperty("internalDate", out var timestamp) && long.TryParse(timestamp.GetString(), out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : DateTimeOffset.UtcNow;
        var attachments = new List<IndexedAttachment>();
        ReadAttachments(payload, attachments);
        return new IndexedMessage(
            id,
            root.TryGetProperty("threadId", out var thread) ? thread.GetString() ?? id : id,
            labels.Contains("SENT") ? "sent" : "received",
            ParseAddress(Header(headers, "From")),
            ParseAddresses(Header(headers, "To")),
            Header(headers, "Subject", "(sin asunto)"),
            root.TryGetProperty("snippet", out var snippet) ? snippet.GetString() ?? string.Empty : string.Empty,
            occurredAt,
            attachments);
    }

    private static Dictionary<string, string> Headers(JsonElement payload)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!payload.TryGetProperty("headers", out var headers)) return result;
        foreach (var item in headers.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var name) || !item.TryGetProperty("value", out var value)) continue;
            var key = name.GetString();
            if (!string.IsNullOrWhiteSpace(key)) result[key] = value.GetString() ?? string.Empty;
        }
        return result;
    }

    private static string Header(Dictionary<string, string> headers, string name, string fallback = "") => headers.TryGetValue(name, out var value) ? value : fallback;

    private static IndexedAddress ParseAddress(string raw)
    {
        var value = raw.Trim();
        var match = Regex.Match(value, "^(?<name>.*?)\\s*<(?<email>[^>]+)>$");
        if (match.Success) return new IndexedAddress(match.Groups["name"].Value.Trim().Trim('"'), NormalizeEmail(match.Groups["email"].Value));
        return new IndexedAddress(string.Empty, NormalizeEmail(value));
    }

    private static IReadOnlyCollection<IndexedAddress> ParseAddresses(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        var matches = Regex.Matches(raw, "(?:(?<name>[^,<]+?)\\s*)?<(?<email>[^>]+)>|(?<email>[A-Z0-9._%+\\-]+@[A-Z0-9.\\-]+)", RegexOptions.IgnoreCase);
        if (matches.Count == 0) return [ParseAddress(raw)];
        return matches.Cast<Match>()
            .Select(match => new IndexedAddress(match.Groups["name"].Value.Trim().Trim('"'), NormalizeEmail(match.Groups["email"].Value)))
            .Where(x => x.Address.Contains('@'))
            .ToArray();
    }

    private static string SerializeAddresses(IReadOnlyCollection<IndexedAddress> addresses) => string.Join('\n', addresses.Select(x => $"{CleanAddressField(x.Name)}\t{x.Address}"));

    private static IReadOnlyCollection<IndexedAddress> DeserializeAddresses(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        return value.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t', 2))
            .Where(parts => parts.Length == 2 && parts[1].Contains('@'))
            .Select(parts => new IndexedAddress(parts[0], NormalizeEmail(parts[1])))
            .ToArray();
    }

    private static string CleanAddressField(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static void ReadAttachments(JsonElement part, List<IndexedAttachment> result)
    {
        var fileName = part.TryGetProperty("filename", out var filename) ? filename.GetString() ?? string.Empty : string.Empty;
        var contentType = part.TryGetProperty("mimeType", out var mimeType) ? mimeType.GetString() ?? "application/octet-stream" : "application/octet-stream";
        if (!string.IsNullOrWhiteSpace(fileName) && part.TryGetProperty("body", out var body) && body.TryGetProperty("attachmentId", out var attachmentId) && !string.IsNullOrWhiteSpace(attachmentId.GetString()))
        {
            var size = body.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var bytes) ? bytes : 0;
            result.Add(new IndexedAttachment(attachmentId.GetString()!, fileName, contentType, size));
        }
        if (part.TryGetProperty("parts", out var parts)) foreach (var child in parts.EnumerateArray()) ReadAttachments(child, result);
    }

    private async Task<HttpClient> CreateClientAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var credential = await database.OAuthCredentials.AsNoTracking().SingleOrDefaultAsync(x => x.MailAccountId == accountId, cancellationToken)
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
        var accessToken = document.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Google no entregó un token de acceso válido.");
        var client = httpClientFactory.CreateClient("Gmail");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static List<TimelineEvent> Timeline(Dictionary<string, List<TimelineEvent>> timelines, string threadId, string email)
    {
        var key = $"{threadId}|{email}";
        if (!timelines.TryGetValue(key, out var events)) timelines[key] = events = [];
        return events;
    }

    private static int? Average(IReadOnlyCollection<int> values) => values.Count == 0 ? null : (int)Math.Round(values.Average());
    private static DateTimeOffset? Max(DateTimeOffset? current, DateTimeOffset value) => !current.HasValue || value > current.Value ? value : current;
    private static string NormalizeEmail(string value) => value.Trim().ToLowerInvariant();
    private static string ContactName(string name, string email) => string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name.Trim().Trim('"');

    private static bool IsNonPersonalAddress(string value)
    {
        var local = (NormalizeEmail(value).Split('@')[0] ?? string.Empty).Replace(".", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        return Regex.IsMatch(local, "^(noreply|donotreply|mailerdaemon|postmaster|newsletter|notifications?|alerts?|marketing|news|info|contacto|contact|soporte|support|ventas|sales|administracion|administrativo|admin|secretaria|recepcion|office|comunicaciones|communications|rrhh|recursoshumanos|facturacion|billing|cobranza|webmaster)$", RegexOptions.IgnoreCase);
    }

    private static bool IsUsefulDocument(string fileName)
    {
        var name = fileName.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (Regex.IsMatch(name, "\\.(png|jpg|jpeg|gif|webp|bmp|svg|ico)$", RegexOptions.IgnoreCase)) return false;
        return !Regex.IsMatch(name, "^(image\\d*|logo|firma|signature|facebook|instagram|linkedin|twitter|x-logo)", RegexOptions.IgnoreCase);
    }

    private static string DocumentType(string fileName, string contentType)
    {
        var name = fileName.ToLowerInvariant();
        if (contentType == "application/pdf" || name.EndsWith(".pdf")) return "PDF";
        if (Regex.IsMatch(name, "\\.(doc|docx|odt|rtf)$")) return "Documento";
        if (Regex.IsMatch(name, "\\.(xls|xlsx|ods|csv)$")) return "Planilla";
        if (Regex.IsMatch(name, "\\.(ppt|pptx|odp)$")) return "Presentación";
        if (Regex.IsMatch(name, "\\.(zip|rar|7z)$")) return "Comprimido";
        if (Regex.IsMatch(name, "\\.(txt|xml|json)$")) return "Texto / datos";
        return contentType.Split('/').LastOrDefault()?.ToUpperInvariant() ?? "Archivo";
    }

    private sealed record IndexedAddress(string Name, string Address);
    private sealed record IndexedAttachment(string AttachmentId, string FileName, string ContentType, long Size);
    private sealed record IndexedMessage(string MessageId, string ThreadId, string Direction, IndexedAddress From, IReadOnlyCollection<IndexedAddress> To, string Subject, string Snippet, DateTimeOffset OccurredAt, IReadOnlyCollection<IndexedAttachment> Attachments);
    private sealed record TimelineEvent(DateTimeOffset At, bool Sent);
    private sealed class SubjectStat { public required string Subject { get; init; } public int Count { get; set; } public DateTimeOffset LastAt { get; set; } }
    private sealed class ContactAccumulator(string email, string name)
    {
        public string Email { get; } = email;
        public string Name { get; set; } = name;
        public HashSet<string> Accounts { get; } = new(StringComparer.CurrentCultureIgnoreCase);
        public int Sent { get; set; }
        public int Received { get; set; }
        public int Replies { get; set; }
        public int Awaiting { get; set; }
        public List<int> ResponseMinutes { get; } = [];
        public DateTimeOffset? LastInteraction { get; set; }
        public Dictionary<string, SubjectStat> Subjects { get; } = new(StringComparer.CurrentCultureIgnoreCase);
        public void AddSubject(string subject, DateTimeOffset at)
        {
            var normalized = Regex.Replace(subject ?? string.Empty, "^\\s*((re|rv|fw|fwd)\\s*:\\s*)+", string.Empty, RegexOptions.IgnoreCase).Trim();
            if (string.IsNullOrWhiteSpace(normalized)) normalized = "(sin asunto)";
            if (!Subjects.TryGetValue(normalized, out var stat)) Subjects[normalized] = stat = new SubjectStat { Subject = normalized };
            stat.Count++;
            if (at > stat.LastAt) stat.LastAt = at;
        }
    }
}
