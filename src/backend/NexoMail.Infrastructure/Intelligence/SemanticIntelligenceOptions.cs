namespace NexoMail.Infrastructure.Intelligence;

public sealed class SemanticIntelligenceOptions
{
    public const string SectionName = "NexoIntelligence:Semantic";

    public bool Enabled { get; set; } = false;
    public int MaxCandidatesPerRequest { get; set; } = 50;
    public int MaxCandidatesPerBatch { get; set; } = 12;
    public int MaxPromptCharacters { get; set; } = 22_000;
    public int MaxConcurrency { get; set; } = 1;
}
