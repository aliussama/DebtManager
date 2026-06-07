namespace DebtManager.Domain.Events;

public sealed record BankTransactionDecisionReverted(
    Guid ImportedId,
    string RevertedDecisionType,
    Guid? RevertedEventId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);

public sealed record BankTransactionDecisionCorrected(
    Guid ImportedId,
    string NewDecisionType,
    string? ApplyMode,
    Guid? TargetId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);

public sealed record BankImportBatchUndoRequested(
    Guid BatchId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);

public sealed record BankImportBatchUndoCompleted(
    Guid BatchId,
    int RevertedCount,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
