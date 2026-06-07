using DebtManager.Domain.Forecasting;

namespace DebtManager.Domain.Projections;

public sealed class ScenarioChangeRecord
{
    public Guid ChangeId { get; set; }
    public ScenarioChangeKind Kind { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public bool IsRemoved { get; set; }
}

public sealed class ScenarioRecord
{
    public Guid ScenarioId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public DateOnly HorizonStart { get; set; }
    public DateOnly HorizonEnd { get; set; }
    public ForecastGranularity Granularity { get; set; } = ForecastGranularity.Monthly;
    public Dictionary<Guid, ScenarioChangeRecord> Changes { get; } = new();
}

public sealed class ScenarioState
{
    public Dictionary<Guid, ScenarioRecord> Scenarios { get; } = new();
}
