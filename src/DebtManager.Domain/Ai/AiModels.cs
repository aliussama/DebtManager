namespace DebtManager.Domain.Ai;

public enum AiProposalStatus { Pending, Approved, Rejected, Executed }

public sealed class AiInsight
{
    public Guid InsightId { get; set; }
    public string InsightCode { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    public string Area { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateOnly RecordedDate { get; set; }
}

public sealed class AiProposal
{
    public Guid ProposalId { get; set; }
    public string ProposalKind { get; set; } = string.Empty;
    public string ProposalJson { get; set; } = "{}";
    public string Reason { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = "Low";
    public AiProposalStatus Status { get; set; } = AiProposalStatus.Pending;
    public string? RejectionReason { get; set; }
    public DateOnly CreatedDate { get; set; }
}

public sealed class AiSettingsRecord
{
    public bool Enabled { get; set; }
    public bool AllowInternetAccess { get; set; }
    public bool AllowAutoProposalGeneration { get; set; }
}
