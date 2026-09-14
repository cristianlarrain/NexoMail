using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NexoMail.Application.Intelligence;
using NexoMail.Infrastructure.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class DependencyInjectionRegression
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var services = new ServiceCollection();
        var infrastructureAssembly = typeof(CommunicationIntelligenceService).Assembly;
        var extensionsType = infrastructureAssembly.GetType(
            "NexoMail.Infrastructure.Intelligence.IntelligenceServiceCollectionExtensions");

        Ensure(extensionsType is not null,
            "Debe existir un punto único de registro DI para Nexo Intelligence.");

        var addMethod = extensionsType!.GetMethod(
            "AddNexoMailIntelligence",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(IServiceCollection)],
            modifiers: null);

        Ensure(addMethod is not null,
            "AddNexoMailIntelligence(IServiceCollection) debe registrar el motor completo.");

        addMethod!.Invoke(null, [services]);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Ensure(scope.ServiceProvider.GetRequiredService<IActionabilityAnalyzer>() is DeterministicActionabilityAnalyzer,
            "DI debe resolver el analizador determinístico por su contrato.");
        Ensure(scope.ServiceProvider.GetRequiredService<IConversationStateResolver>() is DeterministicConversationStateResolver,
            "DI debe resolver el state resolver determinístico por su contrato.");
        Ensure(scope.ServiceProvider.GetRequiredService<IPriorityScorer>() is DeterministicPriorityScorer,
            "DI debe resolver el scorer determinístico por su contrato.");

        var intelligence = scope.ServiceProvider.GetRequiredService<ICommunicationIntelligenceService>();
        Ensure(intelligence is CommunicationIntelligenceService,
            "DI debe resolver el orquestador de Intelligence por su contrato.");

        var now = new DateTimeOffset(2026, 9, 14, 4, 0, 0, TimeSpan.Zero);
        var conversation = new CommunicationConversation(
            "di-conversation",
            [
                new CommunicationMessage(
                    "di-message",
                    "di-conversation",
                    now.AddMinutes(-2),
                    CommunicationDirection.Received,
                    "Generic subject",
                    "sender@external.test",
                    ["user@example.test"],
                    [],
                    IsRead: false,
                    IsDirectRecipient: true,
                    IsAutomated: false,
                    IsBulk: false,
                    HasListUnsubscribe: false,
                    AutoSubmitted: null,
                    Precedence: null)
            ],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "user@example.test" },
            now);

        var result = intelligence.Analyze(conversation);
        Ensure(result.Actionability.IsActionable
               && result.State.State == ConversationWorkState.PendingUser
               && result.Priority.Score > 0
               && result.EngineVersion == "nexo-intelligence/0.1-deterministic",
            "El motor resuelto por DI debe ejecutar el pipeline determinístico completo.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
