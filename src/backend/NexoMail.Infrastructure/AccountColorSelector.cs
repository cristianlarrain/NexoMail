namespace NexoMail.Infrastructure;

public static class AccountColorSelector
{
    public static readonly IReadOnlyList<string> Palette =
    [
        "#0F6B78",
        "#7C3AED",
        "#C026D3",
        "#EA580C",
        "#15803D",
        "#0369A1",
        "#DC2626"
    ];

    public static string Select(IEnumerable<string?> existingColors)
    {
        var usage = existingColors
            .Where(color => !string.IsNullOrWhiteSpace(color))
            .GroupBy(color => color!.ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return Palette
            .Select((color, index) => new { Color = color, Index = index, Count = usage.GetValueOrDefault(color) })
            .OrderBy(candidate => candidate.Count)
            .ThenBy(candidate => candidate.Index)
            .First().Color;
    }
}
