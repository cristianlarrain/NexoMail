using NexoMail.Infrastructure;

namespace NexoMail.Api;

public static class PerspectiveEndpoints
{
    public static RouteGroupBuilder Map(RouteGroupBuilder mail)
    {
        mail.MapPost("/ai/perspective-expansion", async (
            AiWritingService ai,
            AiPerspectiveExpansionRequest request,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text))
                return Results.BadRequest(new { error = "No hay una perspectiva para ampliar." });
            if (request.Text.Length > 2_000)
                return Results.BadRequest(new { error = "La perspectiva es demasiado extensa para ampliarla." });

            try
            {
                return Results.Ok(await ai.GeneratePerspectiveExpansionAsync(
                    request.Text,
                    request.Source ?? "Perspectiva NexoMail",
                    request.Area ?? "General",
                    ct));
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (HttpRequestException)
            {
                return Results.Problem("No fue posible ampliar esta perspectiva con Nexi. Inténtalo nuevamente.", statusCode: 502);
            }
        }).RequireRateLimiting("ai-writing");

        return mail;
    }
}

public sealed record AiPerspectiveExpansionRequest(string Text, string? Source, string? Area);
