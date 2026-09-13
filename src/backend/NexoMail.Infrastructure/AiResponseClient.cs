using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexoMail.Application;

namespace NexoMail.Infrastructure;

public sealed class AiResponseClient(
    IHttpClientFactory httpClientFactory,
    IOptions<AiWritingOptions> options,
    IAiUsageTracker usageTracker,
    IUserContext userContext,
    ILogger<AiResponseClient> logger)
{
    public async Task<string> SendAsync(
        string operationType,
        string instructions,
        string input,
        int maxOutputTokens,
        string reasoningEffort,
        CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("La función de IA todavía no está configurada en el servidor.");

        var model = string.IsNullOrWhiteSpace(settings.Model) ? "gpt-5.6-luna" : settings.Model;
        var payload = JsonSerializer.Serialize(new
        {
            model,
            reasoning = new { effort = reasoningEffort },
            instructions,
            input,
            max_output_tokens = maxOutputTokens
        });

        var started = Stopwatch.GetTimestamp();
        try
        {
            var client = httpClientFactory.CreateClient("OpenAI");
            using var request = new HttpRequestMessage(HttpMethod.Post, "responses")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

            using var response = await client.SendAsync(request, ct);
            var durationMs = ElapsedMilliseconds(started);
            if (!response.IsSuccessStatusCode)
            {
                await TrackSafelyAsync(new AiUsageRecord(
                    userContext.UserId,
                    operationType,
                    model,
                    0,
                    0,
                    null,
                    durationMs,
                    false,
                    $"http_{(int)response.StatusCode}",
                    DateTimeOffset.UtcNow), ct);

                var detail = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"OpenAI rechazó la solicitud ({(int)response.StatusCode}). {Limit(detail, 500)}",
                    null,
                    response.StatusCode);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var root = document.RootElement;
            var output = ExtractOutputText(root).Trim();
            var (inputTokens, outputTokens, reasoningTokens) = ReadUsage(root);

            await TrackSafelyAsync(new AiUsageRecord(
                userContext.UserId,
                operationType,
                model,
                inputTokens,
                outputTokens,
                reasoningTokens,
                durationMs,
                true,
                null,
                DateTimeOffset.UtcNow), ct);

            return output;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            await TrackSafelyAsync(new AiUsageRecord(
                userContext.UserId,
                operationType,
                model,
                0,
                0,
                null,
                ElapsedMilliseconds(started),
                false,
                "provider_exception",
                DateTimeOffset.UtcNow), ct);
            throw;
        }
    }

    private async Task TrackSafelyAsync(AiUsageRecord record, CancellationToken ct)
    {
        try
        {
            await usageTracker.RecordAsync(record, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "No fue posible registrar telemetría de Nexi para {OperationType}.",
                record.OperationType);
        }
    }

    private static (long InputTokens, long OutputTokens, long? ReasoningTokens) ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return (0, 0, null);

        var input = usage.TryGetProperty("input_tokens", out var inputElement) && inputElement.TryGetInt64(out var inputValue)
            ? inputValue
            : 0;
        var output = usage.TryGetProperty("output_tokens", out var outputElement) && outputElement.TryGetInt64(out var outputValue)
            ? outputValue
            : 0;
        long? reasoning = null;
        if (usage.TryGetProperty("output_tokens_details", out var details)
            && details.ValueKind == JsonValueKind.Object
            && details.TryGetProperty("reasoning_tokens", out var reasoningElement)
            && reasoningElement.TryGetInt64(out var reasoningValue))
            reasoning = reasoningValue;

        return (input, output, reasoning);
    }

    private static string ExtractOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var itemType) || itemType.GetString() != "message") continue;
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
            foreach (var part in content.EnumerateArray())
            {
                if (!part.TryGetProperty("type", out var type) || type.GetString() != "output_text") continue;
                if (!part.TryGetProperty("text", out var text) || string.IsNullOrWhiteSpace(text.GetString())) continue;
                if (builder.Length > 0) builder.AppendLine();
                builder.Append(text.GetString());
            }
        }
        return builder.ToString();
    }

    private static long ElapsedMilliseconds(long started) =>
        (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private static string Limit(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
}
