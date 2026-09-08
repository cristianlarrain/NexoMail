using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using NexoMail.Application;
using NexoMail.Domain;
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
        services.AddScoped<AiSearchService>();
        services.AddScoped<AiMailInsightsService>();
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

        mail.MapGet("/messages/{accountId:guid}/{messageId}/thread", async (
            IMailGateway gateway,
            MailReadCache cache,
            IUserContext userContext,
            Guid accountId,
            string messageId,
            CancellationToken ct) =>
        {
            var value = await cache.GetOrCreateAsync(
                userContext.UserId.ToString(),
                "message-thread",
                $"{accountId:N}:{messageId}",
                TimeSpan.FromMinutes(10),
                token => gateway.GetThreadAsync(accountId, messageId, token),
                ct);
            return Results.Ok(value);
        });

        mail.MapGet("/ai/status", (Microsoft.Extensions.Options.IOptions<AiWritingOptions> options) =>
        {
            var settings = options.Value;
            return Results.Ok(new
            {
                configured = !string.IsNullOrWhiteSpace(settings.ApiKey),
                model = string.IsNullOrWhiteSpace(settings.Model) ? "gpt-5.6-luna" : settings.Model
            });
        });

        mail.MapPost("/ai/search", async (
            AiSearchService ai,
            AiSearchRequest request,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return Results.BadRequest(new { error = "Escribe qué quieres buscar." });
            if (request.Query.Length > 500)
                return Results.BadRequest(new { error = "La búsqueda es demasiado extensa." });

            return Results.Ok(await ai.InterpretAsync(request.Query, ct));
        }).RequireRateLimiting("ai-writing");

        mail.MapPost("/messages/{accountId:guid}/{messageId}/ai-summary", async (
            IMailGateway gateway,
            MailReadCache cache,
            IUserContext userContext,
            AiMailInsightsService ai,
            Guid accountId,
            string messageId,
            AiMessageSummaryRequest request,
            CancellationToken ct) =>
        {
            try
            {
                var message = await cache.GetOrCreateAsync(
                    userContext.UserId.ToString(),
                    "message-detail",
                    $"{accountId:N}:{messageId}",
                    TimeSpan.FromMinutes(10),
                    async token => await gateway.GetMessageAsync(accountId, messageId, token) ?? throw new KeyNotFoundException(),
                    ct);

                IReadOnlyCollection<MailThreadMessage>? thread = null;
                if (request.IncludeThread)
                {
                    thread = await cache.GetOrCreateAsync(
                        userContext.UserId.ToString(),
                        "message-thread",
                        $"{accountId:N}:{messageId}",
                        TimeSpan.FromMinutes(10),
                        token => gateway.GetThreadAsync(accountId, messageId, token),
                        ct);
                }

                var result = await cache.GetOrCreateAsync(
                    userContext.UserId.ToString(),
                    "ai-summary",
                    $"{accountId:N}:{messageId}:{request.IncludeThread}",
                    TimeSpan.FromMinutes(15),
                    token => ai.SummarizeMessageAsync(message, thread, request.IncludeThread, token),
                    ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (HttpRequestException)
            {
                return Results.Problem("No fue posible resumir el correo con Nexi. Inténtalo nuevamente.", statusCode: 502);
            }
        }).RequireRateLimiting("ai-writing");

        mail.MapPost("/ai/report", async (
            IMailGateway gateway,
            MailReadCache cache,
            IUserContext userContext,
            AiMailInsightsService ai,
            AiMailReportRequest request,
            CancellationToken ct) =>
        {
            if (!TryReportRange(request.Period, request.LocalDate, out var start, out var end, out var periodLabel))
                return Results.BadRequest(new { error = "El período solicitado no es válido." });

            try
            {
                var cacheScope = $"{request.AccountId?.ToString("N") ?? "all"}:{request.Period}:{request.LocalDate}";
                var report = await cache.GetOrCreateAsync(
                    userContext.UserId.ToString(),
                    "ai-report",
                    cacheScope,
                    TimeSpan.FromMinutes(10),
                    async token =>
                    {
                        var dateSearch = $"after:{start:yyyy/MM/dd} before:{end:yyyy/MM/dd}";
                        var page = await gateway.GetMessagesAsync(new MailQuery(request.AccountId, "inbox", 50, null, dateSearch), token);
                        var summaries = page.Items
                            .Where(item => item.ReceivedAt.Date >= start.ToDateTime(TimeOnly.MinValue).Date && item.ReceivedAt.Date < end.ToDateTime(TimeOnly.MinValue).Date)
                            .OrderByDescending(item => item.ReceivedAt)
                            .Take(25)
                            .ToArray();

                        if (summaries.Length == 0)
                        {
                            var fallback = await gateway.GetMessagesAsync(new MailQuery(request.AccountId, "inbox", 50), token);
                            summaries = fallback.Items
                                .Where(item => item.ReceivedAt.Date >= start.ToDateTime(TimeOnly.MinValue).Date && item.ReceivedAt.Date < end.ToDateTime(TimeOnly.MinValue).Date)
                                .OrderByDescending(item => item.ReceivedAt)
                                .Take(25)
                                .ToArray();
                        }

                        using var semaphore = new SemaphoreSlim(5);
                        var detailTasks = summaries.Select(async item =>
                        {
                            await semaphore.WaitAsync(token);
                            try
                            {
                                try
                                {
                                    return await cache.GetOrCreateAsync(
                                        userContext.UserId.ToString(),
                                        "message-detail",
                                        $"{item.AccountId:N}:{item.ProviderMessageId}",
                                        TimeSpan.FromMinutes(10),
                                        async innerToken => await gateway.GetMessageAsync(item.AccountId, item.ProviderMessageId, innerToken) ?? throw new KeyNotFoundException(),
                                        token);
                                }
                                catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or KeyNotFoundException)
                                {
                                    return null;
                                }
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        });

                        var details = (await Task.WhenAll(detailTasks)).Where(value => value is not null).Cast<MailMessage>().ToArray();
                        return await ai.GenerateReportAsync(periodLabel, details, token);
                    },
                    ct);

                return Results.Ok(report);
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (HttpRequestException)
            {
                return Results.Problem("No fue posible generar el reporte de correo con Nexi. Inténtalo nuevamente.", statusCode: 502);
            }
        }).RequireRateLimiting("ai-writing");

        mail.MapPost("/messages/{accountId:guid}/{messageId}/ai-reply", async (
            IMailGateway gateway,
            MailReadCache cache,
            IUserContext userContext,
            AiWritingService ai,
            Guid accountId,
            string messageId,
            AiReplyRequest request,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Tone))
                return Results.BadRequest(new { error = "Selecciona un tono para la respuesta." });

            var message = await cache.GetOrCreateAsync(
                userContext.UserId.ToString(),
                "message-detail",
                $"{accountId:N}:{messageId}",
                TimeSpan.FromMinutes(10),
                async token => await gateway.GetMessageAsync(accountId, messageId, token) ?? throw new KeyNotFoundException(),
                ct);

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

    private static bool TryReportRange(string period, string? localDate, out DateOnly start, out DateOnly end, out string label)
    {
        var today = DateOnly.TryParseExact(localDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : DateOnly.FromDateTime(DateTime.UtcNow);
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var weekStart = today.AddDays(-daysSinceMonday);

        switch (period.Trim().ToLowerInvariant())
        {
            case "today":
                start = today;
                end = today.AddDays(1);
                label = "Hoy";
                return true;
            case "this_week":
                start = weekStart;
                end = today.AddDays(1);
                label = "Esta semana";
                return true;
            case "last_week":
                end = weekStart;
                start = weekStart.AddDays(-7);
                label = "Semana pasada";
                return true;
            default:
                start = default;
                end = default;
                label = string.Empty;
                return false;
        }
    }
}

public sealed record AiSearchRequest(string Query);
public sealed record AiMessageSummaryRequest(bool IncludeThread);
public sealed record AiMailReportRequest(string Period, string? LocalDate, Guid? AccountId);
public sealed record AiReplyRequest(string Tone, string? Instruction);
public sealed record AiDraftRequest(string Context, string Tone, string? Recipient);
