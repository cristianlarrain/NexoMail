using NexoMail.Application;

var applicationAssembly = typeof(IUserContext).Assembly;
Ensure(applicationAssembly.GetType("NexoMail.Application.Intelligence.IActionabilityAnalyzer") is not null,
    "IActionabilityAnalyzer debe existir como contrato público independiente del proveedor.");
Ensure(applicationAssembly.GetType("NexoMail.Application.Intelligence.IConversationStateResolver") is not null,
    "IConversationStateResolver debe existir como contrato público independiente del proveedor.");
Ensure(applicationAssembly.GetType("NexoMail.Application.Intelligence.IPriorityScorer") is not null,
    "IPriorityScorer debe existir como contrato público independiente del proveedor.");
Ensure(applicationAssembly.GetType("NexoMail.Application.Intelligence.ICommunicationIntelligenceService") is not null,
    "ICommunicationIntelligenceService debe existir como contrato público independiente del proveedor.");

var infrastructureAssembly = typeof(NexoMail.Infrastructure.Data.NexoMailDbContext).Assembly;
Ensure(infrastructureAssembly.GetType("NexoMail.Infrastructure.Intelligence.DeterministicActionabilityAnalyzer") is not null,
    "DeterministicActionabilityAnalyzer debe existir como implementación base independiente del proveedor.");

Console.WriteLine("Nexo Intelligence smoke tests passed.");

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
