using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NexoMail.Application.Intelligence;
using NexoMail.Infrastructure.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class SemanticDependencyInjectionRegression
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    private static void Run()
    {
        var services = new ServiceCollection();
        services.AddNexoMailIntelligence();

        EnsureDescriptor(services, typeof(SemanticPromptBuilder), typeof(SemanticPromptBuilder), ServiceLifetime.Singleton);
        EnsureDescriptor(services, typeof(SemanticResponseParser), typeof(SemanticResponseParser), ServiceLifetime.Singleton);
        EnsureDescriptor(services, typeof(SemanticCommunicationCandidateSource), typeof(SemanticCommunicationCandidateSource), ServiceLifetime.Scoped);
        EnsureDescriptor(services, typeof(ISemanticCommunicationAnalyzer), typeof(OpenAiSemanticCommunicationAnalyzer), ServiceLifetime.Scoped);
        EnsureDescriptor(services, typeof(ISemanticIntelligenceShadowService), typeof(SemanticIntelligenceShadowService), ServiceLifetime.Scoped);

        var programPath = FindRepoFile("src", "backend", "NexoMail.Api", "Program.cs");
        var program = File.ReadAllText(programPath);
        Ensure(program.Contains("Configure<SemanticIntelligenceOptions>", StringComparison.Ordinal)
               && program.Contains("SemanticIntelligenceOptions.SectionName", StringComparison.Ordinal),
            "Program.cs debe enlazar la configuración NexoIntelligence:Semantic sin secretos.");
    }

    private static void EnsureDescriptor(
        IServiceCollection services,
        Type serviceType,
        Type implementationType,
        ServiceLifetime lifetime)
    {
        var descriptor = services.LastOrDefault(item => item.ServiceType == serviceType);
        Ensure(descriptor is not null,
            $"Falta el registro DI para {serviceType.Name}.");
        Ensure(descriptor!.ImplementationType == implementationType,
            $"{serviceType.Name} debe usar {implementationType.Name}.");
        Ensure(descriptor.Lifetime == lifetime,
            $"{serviceType.Name} debe tener lifetime {lifetime}.");
    }

    private static string FindRepoFile(params string[] relativeParts)
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. relativeParts]);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new InvalidOperationException("No fue posible localizar la raíz del repositorio para inspeccionar Program.cs.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
