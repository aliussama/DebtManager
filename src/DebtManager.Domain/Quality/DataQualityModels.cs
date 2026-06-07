namespace DebtManager.Domain.Quality;

public enum DataQualitySeverity
{
    Info,
    Warning,
    Error,
    Critical
}

public enum DataQualityArea
{
    Cash,
    Debts,
    BankImport,
    Budgets,
    Recurring,
    Assets,
    Investments,
    Taxes,
    Goals,
    Retirement,
    Setup,
    Sync
}

public sealed class DataQualityIssue
{
    public Guid IssueId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DataQualitySeverity Severity { get; init; }
    public DataQualityArea Area { get; init; }
    public DateTimeOffset DetectedAt { get; init; }
    public DateOnly? EffectiveDateFrom { get; init; }
    public DateOnly? EffectiveDateTo { get; init; }
    public Dictionary<string, string[]> ImpactedEntityIds { get; init; } = new();
    public string EvidenceJson { get; init; } = "{}";
    public List<DataQualityFixProposal> SuggestedFixes { get; init; } = new();
}

public sealed class DataQualityFixProposal
{
    public string FixKind { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool RequiresUserInput { get; init; }
    public string? InputSchemaJson { get; init; }
    public string? DryRunPreviewJson { get; init; }
}

public sealed class DataQualityScanSummary
{
    public Guid ScanId { get; init; }
    public int TotalIssues { get; init; }
    public Dictionary<DataQualitySeverity, int> CountsBySeverity { get; init; } = new();
    public Dictionary<DataQualityArea, int> CountsByArea { get; init; } = new();
    public List<DataQualityIssue> TopIssues { get; init; } = new();
    public DateTimeOffset GeneratedAt { get; init; }
}
