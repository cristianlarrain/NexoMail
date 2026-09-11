using System.Net;
using System.Text;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Infrastructure;

internal static class AiResponseClientSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync(CancellationToken.None).GetAwaiter().GetResult();

    private static async Task RunAsync(CancellationToken ct)
    {
        var userId = Guid.NewGuid();
        var user = new FakeUserContext(userId);
        var tracker = new CapturingTracker();
        var http = new FakeHttpClientFactory(new QueueHandler(
            SuccessResponse("primera", 120, 30, 7),
            SuccessResponse("segunda", 80, 20, 3)));
        var client = new AiResponseClient(
            http,
            Options.Create(new AiWritingOptions { ApiKey = "test-key", Model = "gpt-5.6-luna" }),
            tracker,
            user,
            NullLogger<AiResponseClient>.Instance);

        var first = await client.SendAsync("mail_summary", "instrucciones", "entrada", 900, "low", ct);
        var second = await client.SendAsync("mail_report", "instrucciones", "entrada", 1200, "low", ct);

        Require(first == "primera" && second == "segunda", "Central client must return extracted output text.");
        Require(tracker.Records.Count == 2, "Every real Responses API call must create one usage record.");
        Require(tracker.Records[0].UserId == userId, "Usage must be attributed to the current user.");
        Require(tracker.Records[0].OperationType == "mail_summary", "Operation type must be stable.");
        Require(tracker.Records[0].InputTokens == 120 && tracker.Records[0].OutputTokens == 30,
            "Provider token counts must be recorded exactly.");
        Require(tracker.Records[0].ReasoningTokens == 7, "Reasoning token details must be captured when present.");
        Require(tracker.Records.All(x => x.Succeeded), "Successful provider calls must be marked successful.");

        var throwingTrackerClient = new AiResponseClient(
            new FakeHttpClientFactory(new QueueHandler(SuccessResponse("continúa", 10, 5, null))),
            Options.Create(new AiWritingOptions { ApiKey = "test-key", Model = "gpt-5.6-luna" }),
            new ThrowingTracker(),
            user,
            NullLogger<AiResponseClient>.Instance);
        var resilient = await throwingTrackerClient.SendAsync("writing_assistant", "i", "x", 100, "low", ct);
        Require(resilient == "continúa", "Telemetry persistence failure must not break a successful Nexi response.");

        var failedTracker = new CapturingTracker();
        var failedClient = new AiResponseClient(
            new FakeHttpClientFactory(new QueueHandler(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("provider unavailable", Encoding.UTF8, "text/plain")
            })),
            Options.Create(new AiWritingOptions { ApiKey = "test-key", Model = "gpt-5.6-luna" }),
            failedTracker,
            user,
            NullLogger<AiResponseClient>.Instance);
        try
        {
            await failedClient.SendAsync("mail_context_analysis", "i", "x", 100, "low", ct);
            throw new InvalidOperationException("Provider failure should throw HttpRequestException.");
        }
        catch (HttpRequestException)
        {
            Require(failedTracker.Records.Count == 1, "Failed provider call must still create one usage record.");
            Require(!failedTracker.Records[0].Succeeded, "Failed provider call must be marked failed.");
            Require(failedTracker.Records[0].InputTokens == 0 && failedTracker.Records[0].OutputTokens == 0,
                "Unknown usage on failed response must not invent tokens.");
        }
    }

    private static HttpResponseMessage SuccessResponse(string text, long input, long output, long? reasoning)
    {
        var reasoningJson = reasoning.HasValue ? $",\"output_tokens_details\":{{\"reasoning_tokens\":{reasoning.Value}}}" : string.Empty;
        var json = $"{{\"output\":[{{\"type\":\"message\",\"content\":[{{\"type\":\"output_text\",\"text\":\"{text}\"}}]}}],\"usage\":{{\"input_tokens\":{input},\"output_tokens\":{output}{reasoningJson}}}}}";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CapturingTracker : IAiUsageTracker
    {
        public List<AiUsageRecord> Records { get; } = [];
        public Task RecordAsync(AiUsageRecord record, CancellationToken ct)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingTracker : IAiUsageTracker
    {
        public Task RecordAsync(AiUsageRecord record, CancellationToken ct) =>
            throw new InvalidOperationException("telemetry unavailable");
    }

    private sealed class FakeUserContext(Guid userId) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid UserId => userId;
        public string Email => "owner@nexomail.test";
        public string DisplayName => "Owner";
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };
    }

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0) throw new InvalidOperationException("No fake response available.");
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
