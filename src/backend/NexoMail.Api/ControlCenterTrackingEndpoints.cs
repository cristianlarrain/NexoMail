using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Api;

public static class ControlCenterTrackingEndpoints
{
    public static RouteGroupBuilder Map(RouteGroupBuilder mail)
    {
        mail.MapGet("/control-center/tracking", async (
            ControlCenterTrackingService service,
            Guid? accountId,
            CancellationToken ct) =>
            Results.Ok(await service.GetTrackedItemsAsync(accountId, ct)));

        mail.MapGet("/control-center/tracking/{accountId:guid}/{messageId}", async (
            ControlCenterTrackingService service,
            Guid accountId,
            string messageId,
            CancellationToken ct) =>
            Results.Ok(new { isTracked = await service.IsTrackedAsync(accountId, messageId, ct) }));

        mail.MapGet("/control-center/state/{accountId:guid}/{messageId}", async (
            NexoMailDbContext database,
            IUserContext userContext,
            Guid accountId,
            string messageId,
            CancellationToken ct) =>
        {
            var normalizedMessageId = messageId.Trim();
            var rows = await database.ControlCenterStates
                .AsNoTracking()
                .Where(x => x.UserId == userContext.UserId
                    && x.AccountId == accountId
                    && x.LastMessageId == normalizedMessageId
                    && !x.ConversationId.StartsWith("manual:")
                    && (x.Status == "resolved" || x.Status == "snoozed"))
                .ToArrayAsync(ct);
            var state = rows.OrderByDescending(x => x.UpdatedAt).FirstOrDefault();
            if (state is null)
                return Results.Ok(new { status = "active", conversationId = (string?)null });

            var status = string.Equals(state.Status, "snoozed", StringComparison.OrdinalIgnoreCase)
                && state.SnoozedUntil is { } until
                && until <= DateTimeOffset.UtcNow
                ? "active"
                : state.Status;
            return Results.Ok(new { status, conversationId = state.ConversationId });
        });

        mail.MapPost("/control-center/tracking/{accountId:guid}/{messageId}", async (
            ControlCenterTrackingService service,
            MailReadCache cache,
            IUserContext userContext,
            Guid accountId,
            string messageId,
            CancellationToken ct) =>
        {
            try
            {
                var updated = await service.TrackAsync(accountId, messageId, ct);
                if (!updated) return Results.NotFound();
                cache.InvalidateAreas(userContext.UserId.ToString(), "control-center");
                return Results.NoContent();
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        mail.MapDelete("/control-center/tracking/{accountId:guid}/{messageId}", async (
            ControlCenterTrackingService service,
            MailReadCache cache,
            IUserContext userContext,
            Guid accountId,
            string messageId,
            CancellationToken ct) =>
        {
            var updated = await service.UntrackAsync(accountId, messageId, ct);
            if (!updated) return Results.NotFound();
            cache.InvalidateAreas(userContext.UserId.ToString(), "control-center");
            return Results.NoContent();
        });

        return mail;
    }
}
