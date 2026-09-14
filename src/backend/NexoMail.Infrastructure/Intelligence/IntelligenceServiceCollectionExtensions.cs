using Microsoft.Extensions.DependencyInjection;
using NexoMail.Application.Intelligence;

namespace NexoMail.Infrastructure.Intelligence;

public static class IntelligenceServiceCollectionExtensions
{
    public static IServiceCollection AddNexoMailIntelligence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IActionabilityAnalyzer, DeterministicActionabilityAnalyzer>();
        services.AddSingleton<IConversationStateResolver, DeterministicConversationStateResolver>();
        services.AddSingleton<IPriorityScorer, DeterministicPriorityScorer>();
        services.AddSingleton<ICommunicationIntelligenceService, CommunicationIntelligenceService>();
        services.AddSingleton<MailMessageIndexConversationAdapter>();
        services.AddSingleton<IntelligenceShadowComparator>();
        services.AddScoped<ICommunicationIntelligenceReader, LocalIndexCommunicationIntelligenceReader>();
        services.AddScoped<IIntelligenceShadowComparisonService, IntelligenceShadowComparisonService>();

        return services;
    }
}