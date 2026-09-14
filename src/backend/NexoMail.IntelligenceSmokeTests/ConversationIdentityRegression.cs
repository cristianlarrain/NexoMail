using System.Runtime.CompilerServices;
using NexoMail.Infrastructure.Intelligence;

namespace NexoMail.IntelligenceSmokeTests;

internal static class ConversationIdentityRegression
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    private static void Run()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        Ensure(
            ConversationIdentity.Normalize(accountId, $"{accountId:N}:thread-123") == "thread-123",
            "Debe quitar sólo el prefijo compuesto de la misma cuenta.");
        Ensure(
            ConversationIdentity.Normalize(accountId, "thread-123") == "thread-123",
            "Un ThreadId simple debe conservarse.");
        Ensure(
            ConversationIdentity.Normalize(accountId, "22222222222222222222222222222222:thread-123")
                == "22222222222222222222222222222222:thread-123",
            "No debe retirar el prefijo de otra cuenta.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
