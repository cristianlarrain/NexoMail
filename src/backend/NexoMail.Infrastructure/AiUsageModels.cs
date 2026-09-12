namespace NexoMail.Infrastructure;

public sealed class AiUsageEventEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long? ReasoningTokens { get; set; }
    public long DurationMs { get; set; }
    public bool Succeeded { get; set; }
    public string? ErrorCategory { get; set; }
    public decimal? InputUsdPerMillion { get; set; }
    public decimal? OutputUsdPerMillion { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public decimal? ClpPerUsd { get; set; }
    public decimal? EstimatedCostClp { get; set; }
}

public sealed class AiUsageMonthlySummaryEntity
{
    public Guid UserId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public long OperationCount { get; set; }
    public long SuccessfulOperations { get; set; }
    public long FailedOperations { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public decimal EstimatedCostClp { get; set; }
    public int ActiveDays { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AiUsageSettingsEntity
{
    public int Id { get; set; } = 1;
    public decimal GreenMaxClp { get; set; } = 1500m;
    public decimal YellowMaxClp { get; set; } = 3000m;
    public decimal ReferenceClpPerUsd { get; set; } = 941.1m;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record AiUsageModelPrice(decimal InputUsdPerMillion, decimal OutputUsdPerMillion);

public sealed class AiUsagePriceCatalogOptions
{
    public const string SectionName = "AI:UsagePricing";
    public decimal DefaultReferenceClpPerUsd { get; set; } = 941.1m;
    public Dictionary<string, AiUsageModelPrice> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record AiUsageCostEstimate(
    decimal InputUsdPerMillion,
    decimal OutputUsdPerMillion,
    decimal EstimatedCostUsd,
    decimal ClpPerUsd,
    decimal EstimatedCostClp);
