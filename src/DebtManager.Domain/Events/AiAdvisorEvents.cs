namespace DebtManager.Domain.Events;

public sealed record AiInsightRecorded(
    Guid InsightId,
    string InsightCode,
    string Severity,
    string Area,
    string Title,
    string Message,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record AiProposalCreated(
    Guid ProposalId,
    string ProposalKind,
    string ProposalJson,
    string Reason,
    string RiskLevel,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record AiProposalApproved(
    Guid ProposalId,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record AiProposalRejected(
    Guid ProposalId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record AiSettingsUpdated(
    bool Enabled,
    bool AllowInternetAccess,
    bool AllowAutoProposalGeneration,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
