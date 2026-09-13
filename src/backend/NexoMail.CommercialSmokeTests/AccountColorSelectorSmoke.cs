using System.Runtime.CompilerServices;
using NexoMail.Infrastructure;

internal static class AccountColorSelectorSmoke
{
    [ModuleInitializer]
    internal static void Verify()
    {
        var first = AccountColorSelector.Select([]);
        var second = AccountColorSelector.Select([first]);
        Ensure(first != second, "Dos cuentas consecutivas deben recibir colores distintos.");
        Ensure(AccountColorSelector.Palette.Contains(first), "El color debe pertenecer a la paleta segura.");

        var repeated = AccountColorSelector.Select(AccountColorSelector.Palette);
        Ensure(AccountColorSelector.Palette.Contains(repeated), "Al agotar la paleta debe usarse un fallback seguro.");

        var leastUsed = AccountColorSelector.Select([first, first, second]);
        Ensure(leastUsed != first, "No debe repetirse el color más utilizado si hay alternativas.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
