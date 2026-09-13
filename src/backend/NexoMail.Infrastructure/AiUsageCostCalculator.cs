using Microsoft.Extensions.Options;

namespace NexoMail.Infrastructure;

public sealed class AiUsageCostCalculator(IOptions<AiUsagePriceCatalogOptions> options)
{
    public AiUsageCostEstimate? Calculate(string model, long inputTokens, long outputTokens, decimal clpPerUsd)
    {
        if (!options.Value.Models.TryGetValue(model, out var price)) return null;

        var inputUsd = inputTokens / 1_000_000m * price.InputUsdPerMillion;
        var outputUsd = outputTokens / 1_000_000m * price.OutputUsdPerMillion;
        var usd = inputUsd + outputUsd;
        return new AiUsageCostEstimate(
            price.InputUsdPerMillion,
            price.OutputUsdPerMillion,
            usd,
            clpPerUsd,
            decimal.Round(usd * clpPerUsd, 4));
    }
}
