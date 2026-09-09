using NexoMail.Application;
using NexoMail.Domain;

namespace NexoMail.Api;

public static class DraftEndpoints
{
    public static RouteGroupBuilder Map(RouteGroupBuilder mail)
    {
        mail.MapPost("/drafts", async (
            IMailGateway gateway,
            MailReadCache cache,
            IUserContext userContext,
            DraftRequest request,
            CancellationToken ct) =>
        {
            if (request.Message.FromAccountId == Guid.Empty)
                return Results.BadRequest(new { error = "Selecciona una cuenta desde la cual guardar el borrador." });

            try
            {
                await gateway.SaveDraftAsync(request.Message.FromAccountId, request.ReplyToMessageId, request.Message, ct);
                cache.InvalidateAreas(userContext.UserId.ToString(), "messages", "message-detail");
                return Results.Accepted();
            }
            catch (NotSupportedException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (HttpRequestException exception)
            {
                return Results.Problem(
                    $"El proveedor no pudo guardar el borrador ({exception.StatusCode?.ToString() ?? "sin código"}).",
                    statusCode: 502);
            }
        });

        mail.MapPut("/drafts/{accountId:guid}/{draftMessageId}", async (
            IMailGateway gateway,
            MailReadCache cache,
            IUserContext userContext,
            Guid accountId,
            string draftMessageId,
            ComposeMessage request,
            CancellationToken ct) =>
        {
            if (accountId == Guid.Empty || string.IsNullOrWhiteSpace(draftMessageId))
                return Results.BadRequest(new { error = "El borrador seleccionado no es válido." });

            try
            {
                await gateway.UpdateDraftAsync(accountId, draftMessageId, request, ct);
                cache.InvalidateAreas(userContext.UserId.ToString(), "messages", "message-detail");
                return Results.Accepted();
            }
            catch (NotSupportedException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (HttpRequestException exception)
            {
                return Results.Problem(
                    $"El proveedor no pudo actualizar el borrador ({exception.StatusCode?.ToString() ?? "sin código"}).",
                    statusCode: 502);
            }
        });

        mail.MapPost("/drafts/{accountId:guid}/{draftMessageId}/send", async (
            IMailGateway gateway,
            MailReadCache cache,
            IUserContext userContext,
            Guid accountId,
            string draftMessageId,
            ComposeMessage request,
            CancellationToken ct) =>
        {
            if (accountId == Guid.Empty || string.IsNullOrWhiteSpace(draftMessageId))
                return Results.BadRequest(new { error = "El borrador seleccionado no es válido." });

            try
            {
                await gateway.SendDraftAsync(accountId, draftMessageId, request, ct);
                cache.InvalidateAreas(userContext.UserId.ToString(), "messages", "message-detail", "message-thread", "control-center", "control-center-activity");
                return Results.Accepted();
            }
            catch (NotSupportedException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (HttpRequestException exception)
            {
                return Results.Problem(
                    $"El proveedor no pudo enviar el borrador ({exception.StatusCode?.ToString() ?? "sin código"}).",
                    statusCode: 502);
            }
        });

        return mail;
    }
}

public sealed record DraftRequest(ComposeMessage Message, string? ReplyToMessageId);
