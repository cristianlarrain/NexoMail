using NexoMail.Application;
using NexoMail.Infrastructure;

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
                cache.Invalidate(userContext.UserId.ToString());
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
            cache.Invalidate(userContext.UserId.ToString());
            return Results.NoContent();
        });

        return mail;
    }
}
