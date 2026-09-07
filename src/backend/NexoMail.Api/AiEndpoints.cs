using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using NexoMail.Application;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Google;

namespace NexoMail.Api;

public static class AiEndpoints
{
    public static IServiceCollection AddNexoMailAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiWritingOptions>(configuration.GetSection(AiWritingOptions.SectionName));
        services.PostConfigure<AiWritingOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.ApiKey))
                options.ApiKey = configuration["OPENAI_API_KEY"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(options.Model))
                options.Model = "gpt-5.6-luna";
        });

        services.AddHttpClient("OpenAI", client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.Timeout = TimeSpan.FromSeconds(35);
        });
        services.AddScoped<AiWritingService>();
        services.AddScoped<ControlCenterTrackingService>();
        services.AddScoped<GmailDraftProvider>();
        services.AddScoped<GmailMetadataIndexService>();
        services.AddScoped<IMailDraftProvider>(services => services.GetRequiredService<GmailDraftProvider>());
        services.AddRateLimiter(options => options.AddPolicy("ai-writing", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(10),
                    QueueLimit = 0,
                    AutoReplenishment = true
                })));
        return services;
    }

    public static RouteGroupBuilder MapNexoMailAi(this RouteGroupBuilder mail)
    {
        ControlCenterTrackingEndpoints.Map(mail);
        DraftEndpoints.Map(mail);
        MetadataIndexEndpoints.Map(mail);

        mail.MapGet("/ai/status", (Microsoft.Extensions.Options.IOptions<AiWritingOptions> options) =>
        {
            var settings = options.Value;
            return Results.Ok(new
            {
                configured = !string.IsNullOrWhiteSpace(settings.ApiKey),
                model = string.IsNullOrWhiteSpace(settings.Model) ? "gpt-5.6-luna" : settings.Model
            });
        });

        mail.MapPost("/messages/{accountId:guid}/{messageId}/ai-reply", async (
            IMailGateway gateway,
            AiWritingService ai,
            Guid accountId,
            string messageId,
            AiReplyRequest request,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Tone))
                return Results.BadRequest(new { error = "Selecciona un tono para la respuesta." });

            var message = await gateway.GetMessageAsync(accountId, messageId, ct);
            if (message is null) return Results.NotFound();

            try
            {
                return Results.Ok(await ai.GenerateReplyAsync(message, request.Tone, request.Instruction, ct));
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (HttpRequestException)
            {
                return Results.Problem("No fue posible generar la respuesta con IA. Inténtalo nuevamente.", statusCode: 502);
            }
        }).RequireRateLimiting("ai-writing");

        mail.MapPost("/ai/draft", async (
            AiWritingService ai,
            AiDraftRequest request,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Context))
                return Results.BadRequest(new { error = "Escribe brevemente qué quieres comunicar." });
            if (request.Context.Length > 3_500)
                return Results.BadRequest(new { error = "El contexto es demasiado extenso. Resume la idea principal." });
            if (string.IsNullOrWhiteSpace(request.Tone))
                return Results.BadRequest(new { error = "Selecciona un tono para el correo." });

            try
            {
                return Results.Ok(await ai.GenerateDraftAsync(request.Context, request.Tone, request.Recipient, ct));
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (HttpRequestException)
            {
                return Results.Problem("No fue posible generar el borrador con IA. Inténtalo nuevamente.", statusCode: 502);
            }
        }).RequireRateLimiting("ai-writing");

        return mail;
    }
}

public sealed record AiReplyRequest(string Tone, string? Instruction);
public sealed record AiDraftRequest(string Context, string Tone, string? Recipient);
