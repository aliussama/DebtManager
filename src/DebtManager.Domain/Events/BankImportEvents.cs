namespace DebtManager.Domain.Events;

// --- Import Profile Events ---

public sealed record BankImportProfileCreated(
    Guid ProfileId,
    string Name,
    string MappingJson,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record BankImportProfileModified(
    Guid ProfileId,
    string MappingJson,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);

public sealed record BankImportProfileArchived(
    Guid ProfileId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);

// --- Import Batch Events ---

public sealed record BankImportBatchStarted(
    Guid BatchId,
    Guid ProfileId,
    Guid AccountId,
    string FileName,
    string FileHashSha256,
    int RowCount,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record BankTransactionImported(
    Guid ImportedId,
    Guid BatchId,
    Guid AccountId,
    DateOnly TxnDate,
    decimal Amount,
    string CurrencyCode,
    string Description,
    string Reference,
    string Counterparty,
    string Direction,
    string RawJson,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record BankImportBatchCompleted(
    Guid BatchId,
    int ImportedCount,
    int SkippedDuplicatesCount,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

// --- Reconciliation Events ---

public sealed record BankTransactionMatched(
    Guid ImportedId,
    Guid MatchedEventId,
    string MatchType,
    decimal Confidence,
    DateOnly EffectiveDate,
    string? Notes
) : DomainEvent(EffectiveDate);

public sealed record BankTransactionIgnored(
    Guid ImportedId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);

public sealed record BankTransactionApplied(
    Guid ImportedId,
    Guid AppliedEventId,
    string AppliedType,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
