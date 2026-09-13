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

        mail.MapPost("/ai/greeting-image", async (
            AiWritingService ai,
            AiGreetingImageRequest request,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.MessageText))
                return Results.BadRequest(new { error = "Escribe primero el mensaje que quieres acompañar con una imagen." });
            if (request.MessageText.Length > 3_500)
                return Results.BadRequest(new { error = "El mensaje es demasiado extenso para generar una imagen." });

            try
            {
                return Results.Ok(await ai.GenerateGreetingImageAsync(request.MessageText, request.Style, ct));
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (HttpRequestException)
            {
                return Results.Problem("No fue posible generar la imagen con Nexi. Inténtalo nuevamente.", statusCode: 502);
            }
        }).RequireRateLimiting("ai-writing");

        return mail;
    }
}

public sealed record AiPerspectiveExpansionRequest(string Text, string? Source, string? Area);
public sealed record AiGreetingImageRequest(string MessageText, string? Style);