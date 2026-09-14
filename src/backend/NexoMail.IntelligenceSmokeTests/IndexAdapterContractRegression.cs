using System.Runtime.CompilerServices;
using NexoMail.Infrastructure.Data;

namespace NexoMail.IntelligenceSmokeTests;

internal static class IndexAdapterContractRegression
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var adapterType = typeof(NexoMailDbContext).Assembly.GetType(
            "NexoMail.Infrastructure.Intelligence.MailMessageIndexConversationAdapter");

        Ensure(adapterType is not null,
            "Debe existir un adaptador explícito desde MailMessageIndex al modelo de Nexo Intelligence.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
