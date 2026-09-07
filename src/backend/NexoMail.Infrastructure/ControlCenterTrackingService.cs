using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure;

/// <summary>
/// Stores only follow-up metadata. Message bodies and attachments remain provider-resident.
/// </summary>
public sealed class ControlCenterTrackingService(
    NexoMailDbContext database,
    IUserContext userContext,
    IMailGateway gateway)
{
    private const string ManualPrefix = "manual:";
    private const int MaximumTrackedItems = 100;

    public async Task<bool> IsTrackedAsync(Guid accountId, string messageId, CancellationToken cancellationToken)
    {
        var key = ManualKey(messageId);
        return await database.ControlCenterStates
            .AsNoTracking()
            .AnyAsync(x => x.UserId == userContext.UserId
                && x.AccountId == accountId
                && x.ConversationId == key
                && x.Status == "tracked", cancellationToken);
    }

    public async Task<bool> TrackAsync(Guid accountId, string messageId, CancellationToken cancellationToken)
    {
        var accountExists = await database.MailAccounts
            .AsNoTracking()
            .AnyAsync(x => x.Id == accountId && x.UserId == userContext.UserId && x.IsActive, cancellationToken);
        if (!accountExists) return false;

        var normalizedMessageId = messageId.Trim();
        var key = ManualKey(normalizedMessageId);
        var state = await database.ControlCenterStates.SingleOrDefaultAsync(
            x => x.UserId == userContext.UserId && x.AccountId == accountId && x.ConversationId == key,
            cancellationToken);

        if (state is null)
        {
            state = new ControlCenterStateEntity
            {
                Id = Guid.NewGuid(),
                UserId = userContext.UserId,
                AccountId = accountId,
                ConversationId = key,
            };
            database.ControlCenterStates.Add(state);
        }

        state.LastMessageId = normalizedMessageId;
        state.Status = "tracked";
        state.SnoozedUntil = null;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UntrackAsync(Guid accountId, string messageId, CancellationToken cancellationToken)
    {
        var accountExists = await database.MailAccounts
            .AsNoTracking()
            .AnyAsync(x => x.Id == accountId && x.UserId == userContext.UserId && x.IsActive, cancellationToken);
        if (!accountExists) return false;

        var key = ManualKey(messageId);
        var state = await database.ControlCenterStates.SingleOrDefaultAsync(
            x => x.UserId == userContext.UserId && x.AccountId == accountId && x.ConversationId == key,
            cancellationToken);
        if (state is null) return true;

        database.ControlCenterStates.Remove(state);
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyCollection<ControlCenterPendingItem>> GetTrackedItemsAsync(Guid? accountId, CancellationToken cancellationToken)
    {
        var stateQuery = database.ControlCenterStates
            .AsNoTracking()
            .Where(x => x.UserId == userContext.UserId
                && x.Status == "tracked"
                && x.ConversationId.StartsWith(ManualPrefix));
        if (accountId.HasValue) stateQuery = stateQuery.Where(x => x.AccountId == accountId.Value);

        // SQLite cannot translate ORDER BY over DateTimeOffset. Materialize the small
        // metadata set first, then order and cap it safely in memory.
        var stateRows = await stateQuery.ToArrayAsync(cancellationToken);
        var states = stateRows
            .OrderByDescending(x => x.UpdatedAt)
            .Take(MaximumTrackedItems)
            .ToArray();
        if (states.Length == 0) return [];

        var accountIds = states.Select(x => x.AccountId).Distinct().ToArray();
        var accounts = await database.MailAccounts
            .AsNoTracking()
            .Where(x => x.UserId == userContext.UserId && x.IsActive && accountIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var values = new List<ControlCenterPendingItem>(states.Length);
        foreach (var state in states)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!accounts.TryGetValue(state.AccountId, out var account)) continue;

            MailMessage? message;
            try
            {
                message = await gateway.GetMessageAsync(state.AccountId, state.LastMessageId, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or KeyNotFoundException)
            {
                continue;
            }

            if (message is null || message.FolderId is "drafts" or "trash" or "spam") continue;
            var sent = string.Equals(message.FolderId, "sent", StringComparison.OrdinalIgnoreCase);
            var counterpart = sent
                ? DisplayAddress(message.To.FirstOrDefault())
                : DisplayAddress(message.From);

            values.Add(new ControlCenterPendingItem(
                state.AccountId,
                account.DisplayName,
                account.Color,
                message.ProviderMessageId,
                state.ConversationId,
                sent ? "sent" : "received",
                counterpart,
                string.IsNullOrWhiteSpace(message.Subject) ? "(sin asunto)" : message.Subject,
                message.ReceivedAt,
                message.IsRead));
        }

        return values.OrderBy(x => x.Since).ToArray();
    }

    private static string ManualKey(string messageId)
    {
        var trimmed = messageId.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) throw new InvalidOperationException("El correo no tiene un identificador válido.");
        return $"{ManualPrefix}{trimmed}";
    }

    private static string DisplayAddress(MailAddress? address)
    {
        if (address is null) return "Destinatario";
        return string.IsNullOrWhiteSpace(address.Name) ? address.Address : address.Name;
    }
}
