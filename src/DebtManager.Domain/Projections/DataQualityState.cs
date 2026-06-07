namespace DebtManager.Domain.Projections;

/// <summary>
/// State of the data quality subsystem derived from events.
/// </summary>
public sealed class DataQualityState
{
    public Dictionary<Guid, DataQualityScanRecord> Scans { get; } = new();
    public HashSet<Guid> AcknowledgedIssueIds { get; } = new();
    public HashSet<Guid> ResolvedIssueIds { get; } = new();
    public Dictionary<Guid, List<Guid>> AppliedFixesByIssue { get; } = new();
}

public sealed class DataQualityScanRecord
{
    public Guid ScanId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public string AppVersion { get; set; } = string.Empty;
    public string RuleSetVersion { get; set; } = string.Empty;
    public string SummaryJson { get; set; } = "{}";
}
