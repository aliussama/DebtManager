using DebtManager.Domain.Ai;

namespace DebtManager.Domain.Projections;

public sealed class AiAdvisorState
{
    public Dictionary<Guid, AiInsight> Insights { get; } = new();
    public Dictionary<Guid, AiProposal> Proposals { get; } = new();
    public AiSettingsRecord Settings { get; set; } = new();
}
