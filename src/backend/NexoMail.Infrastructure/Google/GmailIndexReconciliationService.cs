using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Google;

/// <summary>
/// Compares Gmail's complete provider-visible message ID set with NexoMail's metadata index.
/// Missing rows may be repaired; orphan rows are diagnostic only and are never deleted here.
/// </summary>
public sealed class GmailIndexReconciliationService(
    NexoMailDbContext database,
    GmailMetadataIndexService metadataIndex,
    IUserContext userContext,
    ILogger<GmailIndexReconciliationService>? logger = null)
{
    private const string ReconciliationErrorCode = "reconcile_error";

    public async Task<MailIndexReconciliationResult> ReconcileAsync(
        Guid accountId,
        bool repairMissing,
        CancellationToken cancellationToken)
    {
        var account = await database.MailAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == accountId &&
                     x.UserId == userContext.UserId &&
                     x.IsActive &&
                     x.Provider == MailProviderType.Gmail,
                cancellationToken)
            ?? throw new InvalidOperationException("La cuenta Gmail no existe, no está activa o no pertenece al usuario autenticado.");

        try
        {
            var providerIds = (await metadataIndex.ListAllProviderMessageIdsAsync(account.Id, cancellationToken))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            var rawIndexedIds = await database.MailMessageIndex
                .AsNoTracking()
                .Where(x => x.UserId == userContext.UserId && x.AccountId == account.Id)
                .Select(x => x.ProviderMessageId)
                .ToArrayAsync(cancellationToken);

            var initial = Compare(account.Id, providerIds, rawIndexedIds, repaired: 0, DateTimeOffset.UtcNow);
            var repaired = 0;

            if (repairMissing && initial.MissingProviderIds.Count > 0)
            {
                repaired = await metadataIndex.RepairMissingMessageIdsAsync(
                    userContext.UserId,
                    account.Id,
                    initial.MissingProviderIds,
                    cancellationToken);

                rawIndexedIds = await database.MailMessageIndex
                    .AsNoTracking()
                    .Where(x => x.UserId == userContext.UserId && x.AccountId == account.Id)
                    .Select(x => x.ProviderMessageId)
                    .ToArrayAsync(cancellationToken);
            }

            var reconciledAt = DateTimeOffset.UtcNow;
            var result = Compare(account.Id, providerIds, rawIndexedIds, repaired, reconciledAt);
            var state = await GetOrCreateStateAsync(account.Id, userContext.UserId, cancellationToken);
            state.LastReconciledAt = reconciledAt;
            state.LastReconciliationErrorCode = null;
            state.IndexedMessageCount = rawIndexedIds.Length;
            await database.SaveChangesAsync(cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            try
            {
                var state = await GetOrCreateStateAsync(account.Id, userContext.UserId, cancellationToken);
                state.LastReconciliationErrorCode = ReconciliationErrorCode;
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (Exception statusException)
            {
                logger?.LogWarning(statusException, "No fue posible persistir el error de reconciliación Gmail para {AccountId}.", account.Id);
            }

            logger?.LogWarning(exception, "Falló la reconciliación Gmail para la cuenta {AccountId}.", account.Id);
            throw;
        }
    }

    private static MailIndexReconciliationResult Compare(
        Guid accountId,
        IReadOnlyCollection<string> providerIds,
        IReadOnlyCollection<string> rawIndexedIds,
        int repaired,
        DateTimeOffset reconciledAt)
    {
        var providerSet = providerIds.ToHashSet(StringComparer.Ordinal);
        var indexedSet = rawIndexedIds.ToHashSet(StringComparer.Ordinal);
        var missing = providerSet.Except(indexedSet, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var orphan = indexedSet.Except(providerSet, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var duplicates = rawIndexedIds
            .GroupBy(x => x, StringComparer.Ordinal)
            .Sum(group => Math.Max(0, group.Count() - 1));

        return new MailIndexReconciliationResult(
            accountId,
            providerSet.Count,
            rawIndexedIds.Count,
            missing,
            orphan,
            duplicates,
            repaired,
            reconciledAt,
            missing.Length == 0 && orphan.Length == 0 && duplicates == 0);
    }

    private async Task<MailIndexStateEntity> GetOrCreateStateAsync(Guid accountId, Guid userId, CancellationToken cancellationToken)
    {
        var state = await database.MailIndexStates.SingleOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);
        if (state is not null) return state;

        state = new MailIndexStateEntity
        {
            AccountId = accountId,
            UserId = userId,
            LastIndexedAt = DateTimeOffset.MinValue,
            WindowDays = 90,
            IndexedMessageCount = 0,
        };
        database.MailIndexStates.Add(state);
        return state;
    }
}
