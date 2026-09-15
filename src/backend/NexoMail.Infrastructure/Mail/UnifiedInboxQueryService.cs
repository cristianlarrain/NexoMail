using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Mail;

public sealed class UnifiedInboxQueryService(
    NexoMailDbContext database,
    IUserContext userContext)
{
    private sealed record InboxCursor(DateTimeOffset SnapshotAt, int Offset);

    public async Task<PagedResult<MailSummary>> GetMessagesAsync(
        MailQuery query,
        CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var folder = NormalizeFolder(query.FolderId);
        var take = Math.Clamp(query.Take, 1, 100);
        var cursor = DecodeCursor(query.Cursor);
        var snapshotAt = cursor?.SnapshotAt ?? DateTimeOffset.UtcNow;
        var offset = Math.Max(0, cursor?.Offset ?? 0);

        var activeAccountQuery = database.MailAccounts
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive);
        if (query.AccountId.HasValue)
            activeAccountQuery = activeAccountQuery.Where(x => x.Id == query.AccountId.Value);

        var activeAccountIds = await activeAccountQuery
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        if (activeAccountIds.Length == 0)
            return new PagedResult<MailSummary>([]);

        var indexedQuery = database.MailMessageIndex
            .AsNoTracking()
            .Where(x => x.UserId == userId && activeAccountIds.Contains(x.AccountId));

        indexedQuery = ApplyFolder(indexedQuery, folder, userId);

        var normalizedSearch = query.Search?.Trim() ?? string.Empty;
        if (string.Equals(normalizedSearch, "is:unread", StringComparison.OrdinalIgnoreCase))
        {
            indexedQuery = indexedQuery.Where(x => x.IsUnread);
        }
        else if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var term = normalizedSearch.ToLowerInvariant();
            indexedQuery = indexedQuery.Where(x =>
                x.FromName.ToLower().Contains(term) ||
                x.FromAddress.ToLower().Contains(term) ||
                x.Subject.ToLower().Contains(term) ||
                x.Snippet.ToLower().Contains(term));
        }

        // SQLite cannot reliably order DateTimeOffset server-side across all supported
        // EF versions. Filtering remains relational; deterministic ordering/paging is
        // applied after materialization so SQLite development and SQL Server production
        // follow the same observable contract.
        var candidateRows = await indexedQuery.ToArrayAsync(cancellationToken);
        var ordered = candidateRows
            .Where(x => x.OccurredAt <= snapshotAt)
            .OrderByDescending(x => x.OccurredAt)
            .ThenBy(x => x.AccountId)
            .ThenBy(x => x.ProviderMessageId, StringComparer.Ordinal)
            .Skip(offset)
            .Take(take + 1)
            .ToArray();

        var hasMore = ordered.Length > take;
        var pageRows = hasMore ? ordered[..take] : ordered;
        var items = pageRows.Select(x => ToSummary(x, folder)).ToArray();
        var nextCursor = hasMore
            ? EncodeCursor(new InboxCursor(snapshotAt, offset + items.Length))
            : null;

        return new PagedResult<MailSummary>(items, nextCursor);
    }

    public async Task<IReadOnlyCollection<MailSummary>> ResolveAsync(
        IReadOnlyCollection<MailMessageReference> references,
        CancellationToken cancellationToken)
    {
        if (references.Count == 0) return [];

        var userId = userContext.UserId;
        var activeAccountIds = await database.MailAccounts
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        if (activeAccountIds.Length == 0) return [];

        var requested = references
            .Where(x => activeAccountIds.Contains(x.AccountId) && !string.IsNullOrWhiteSpace(x.ProviderMessageId))
            .Select(x => new MailMessageReference(x.AccountId, x.ProviderMessageId.Trim()))
            .Distinct()
            .ToArray();
        if (requested.Length == 0) return [];

        var requestedAccountIds = requested.Select(x => x.AccountId).Distinct().ToArray();
        var requestedMessageIds = requested.Select(x => x.ProviderMessageId).Distinct(StringComparer.Ordinal).ToArray();

        var rows = await database.MailMessageIndex
            .AsNoTracking()
            .Where(x => x.UserId == userId &&
                        requestedAccountIds.Contains(x.AccountId) &&
                        requestedMessageIds.Contains(x.ProviderMessageId))
            .ToArrayAsync(cancellationToken);

        var lookup = rows.ToDictionary(
            x => (x.AccountId, x.ProviderMessageId),
            x => x);

        var result = new List<MailSummary>(requested.Length);
        var seen = new HashSet<(Guid AccountId, string ProviderMessageId)>();
        foreach (var reference in requested)
        {
            var key = (reference.AccountId, reference.ProviderMessageId);
            if (!seen.Add(key)) continue;
            if (lookup.TryGetValue(key, out var row))
                result.Add(ToSummary(row, FolderFor(row)));
        }
        return result;
    }

    private IQueryable<MailMessageIndexEntity> ApplyFolder(
        IQueryable<MailMessageIndexEntity> query,
        string folder,
        Guid userId) => folder switch
    {
        "inbox" => query
            .Where(x => x.IsInbox && !x.IsSpam && !x.IsTrash)
            .Where(x => !database.IgnoredSenders.Any(ignored =>
                ignored.UserId == userId &&
                ignored.AccountId == x.AccountId &&
                ignored.SenderAddress.ToLower() == x.FromAddress.ToLower())),
        "ignored" => query
            .Where(x => x.IsInbox && !x.IsSpam && !x.IsTrash)
            .Where(x => database.IgnoredSenders.Any(ignored =>
                ignored.UserId == userId &&
                ignored.AccountId == x.AccountId &&
                ignored.SenderAddress.ToLower() == x.FromAddress.ToLower())),
        "sent" => query.Where(x => x.IsSent && !x.IsTrash),
        "drafts" => query.Where(x => x.IsDraft && !x.IsTrash),
        "spam" => query.Where(x => x.IsSpam),
        "trash" => query.Where(x => x.IsTrash),
        "archive" => query.Where(x => !x.IsInbox && !x.IsSent && !x.IsDraft && !x.IsSpam && !x.IsTrash),
        _ => query.Where(x => x.IsInbox && !x.IsSpam && !x.IsTrash)
    };

    private static MailSummary ToSummary(MailMessageIndexEntity row, string folder) => new(
        row.ProviderMessageId,
        row.AccountId,
        string.IsNullOrWhiteSpace(row.FromName) ? row.FromAddress : row.FromName,
        row.FromAddress,
        string.IsNullOrWhiteSpace(row.Subject) ? "(sin asunto)" : row.Subject,
        row.Snippet,
        row.OccurredAt,
        !row.IsUnread,
        row.HasAttachments,
        folder);

    private static string FolderFor(MailMessageIndexEntity row)
    {
        if (row.IsTrash) return "trash";
        if (row.IsSpam) return "spam";
        if (row.IsDraft) return "drafts";
        if (row.IsSent) return "sent";
        if (row.IsInbox) return "inbox";
        return "archive";
    }

    private static string NormalizeFolder(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "inbox" or "ignored" or "sent" or "drafts" or "spam" or "trash" or "archive"
            ? normalized
            : "inbox";
    }

    private static string EncodeCursor(InboxCursor cursor)
    {
        var json = JsonSerializer.Serialize(cursor);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static InboxCursor? DecodeCursor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var cursor = JsonSerializer.Deserialize<InboxCursor>(json);
            if (cursor is null || cursor.Offset < 0)
                throw new InvalidOperationException();
            return cursor;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException("El cursor de bandeja no es válido.", exception);
        }
    }
}
