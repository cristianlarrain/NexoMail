using System.Net;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Application.Intelligence;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class SemanticAnalyzerBatchingRegression
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var tracker = new RecordingUsageTracker();
        var handler = new SemanticFakeHandler();
        var client = BuildClient(handler, tracker);
        var analyzer = new OpenAiSemanticCommunicationAnalyzer(
            client,
            new SemanticPromptBuilder(),
            new SemanticResponseParser(),
            Options.Create(new SemanticIntelligenceOptions
            {
                MaxCandidatesPerBatch = 12,
                MaxPromptCharacters = 22_000,
                MaxConcurrency = 1
            }));

        var candidates = Enumerable.Range(1, 13)
            .Select(index => Candidate($"c{index:00}"))
            .ToArray();

        var results = await analyzer.AnalyzeAsync(candidates, CancellationToken.None);

        Ensure(handler.RequestBodies.Count == 2,
            "Trece candidatos con máximo 12 por lote deben producir exactamente dos llamadas HTTP.");
        Ensure(handler.BatchSizes.SequenceEqual(new[] { 12, 1 }),
            "Ninguna llamada debe superar MaxCandidatesPerBatch.");
        Ensure(results.Select(x => x.CorrelationId).SequenceEqual(candidates.Select(x => x.CorrelationId)),
            "El adaptador debe restaurar el orden original aunque el proveedor responda al revés.");
        Ensure(results.All(x => !x.Assessment.IsUncertain && x.Assessment.ActionType == CommunicationActionType.None),
            "Los resultados válidos del fake deben mapearse por correlación.");
        Ensure(tracker.Records.Count == 2
               && tracker.Records.All(record => record.OperationType == "intelligence_semantic_classification"),
            "Cada lote debe registrarse con la operación semántica específica.");
        Ensure(handler.RequestBodies.All(body => !body.Contains("FULL_BODY_SENTINEL", StringComparison.Ordinal)),
            "El adaptador no debe introducir cuerpo completo fuera de los candidatos minimizados.");

        var failingTracker = new RecordingUsageTracker();
        var failingHandler = new SemanticFakeHandler(failCallNumber: 2);
        var failingAnalyzer = new OpenAiSemanticCommunicationAnalyzer(
            BuildClient(failingHandler, failingTracker),
            new SemanticPromptBuilder(),
            new SemanticResponseParser(),
            Options.Create(new SemanticIntelligenceOptions
            {
                MaxCandidatesPerBatch = 12,
                MaxPromptCharacters = 22_000,
                MaxConcurrency = 1
            }));

        var failureResults = await failingAnalyzer.AnalyzeAsync(candidates, CancellationToken.None);
        Ensure(failureResults.Take(12).All(x => !x.Assessment.IsUncertain),
            "Un lote exitoso debe conservar sus resultados aunque otro lote falle.");
        var failed = failureResults[12];
        Ensure(failed.Assessment.IsUncertain
               && failed.Assessment.ActionType == CommunicationActionType.Unknown
               && failed.Assessment.ReasonCodes.Contains("SEMANTIC_PROVIDER_FAILURE"),
            "Sólo los candidatos del lote fallido deben degradar a incertidumbre de proveedor.");
    }

    private static AiResponseClient BuildClient(HttpMessageHandler handler, RecordingUsageTracker tracker) =>
        new(
            new FakeHttpClientFactory(handler),
            Options.Create(new AiWritingOptions { ApiKey = "test-key", Model = "test-model" }),
            tracker,
            new TestUserContext(),
            NullLogger<AiResponseClient>.Instance);

    private static SemanticCommunicationCandidate Candidate(string id) => new(
        id, $"conv-{id}", $"msg-{id}", DateTimeOffset.UtcNow,
        "Subject", $"Snippet {id}", [], IsRead: true, IsDirectRecipient: true,
        ReplyDiscouragedSender: false, Categories: [], StructuralReasonCodes: ["SEMANTIC_REVIEW_REQUIRED"]);

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class SemanticFakeHandler(int? failCallNumber = null) : HttpMessageHandler
    {
        private int _callCount;
        public List<string> RequestBodies { get; } = [];
        public List<int> BatchSizes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _callCount++;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(body);

            using var outer = JsonDocument.Parse(body);
            var input = outer.RootElement.GetProperty("input").GetString() ?? "{}";
            using var inner = JsonDocument.Parse(input);
            var ids = inner.RootElement.GetProperty("candidates")
                .EnumerateArray()
                .Select(element => element.GetProperty("correlationId").GetString()!)
                .ToArray();
            BatchSizes.Add(ids.Length);

            if (failCallNumber == _callCount)
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("rate limited", Encoding.UTF8, "text/plain")
                };

            var semanticOutput = JsonSerializer.Serialize(new
            {
                results = ids.Reverse().Select(id => new
                {
                    correlationId = id,
                    actionType = "None",
                    requiresAction = false,
                    confidence = 0.91,
                    deadline = (string?)null,
                    reasonCodes = new[] { "INFORMATIONAL" },
                    isUncertain = false
                })
            });
            var responseBody = JsonSerializer.Serialize(new
            {
                output = new[]
                {
                    new
                    {
                        type = "message",
                        content = new[] { new { type = "output_text", text = semanticOutput } }
                    }
                },
                usage = new { input_tokens = 100, output_tokens = 20 }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };
    }

    private sealed class RecordingUsageTracker : IAiUsageTracker
    {
        public List<AiUsageRecord> Records { get; } = [];
        public Task RecordAsync(AiUsageRecord record, CancellationToken ct)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class TestUserContext : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid UserId => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public string Email => "user@example.test";
        public string DisplayName => "Semantic Adapter Test";
    }
}
