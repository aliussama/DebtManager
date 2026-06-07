using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Events;

public sealed record IncomeRecorded(
    Guid AccountId,
    Money Amount,
    DateOnly EffectiveDate,
    string Source
) : IDomainEvent;

public sealed record ExpenseRecorded(
    Guid AccountId,
    Money Amount,
    DateOnly EffectiveDate,
    string Category,
    string Notes
) : IDomainEvent;

/// <summary>
/// Immutable event: A split expense was recorded across multiple categories.
/// TotalAmount must equal sum of Lines[].Amount (validated by handler).
/// </summary>
public sealed record SplitExpenseRecorded(
    Guid ParentId,
    Guid AccountId,
    Money TotalAmount,
    DateOnly EffectiveDate,
    IReadOnlyList<SplitLine> Lines,
    string? Notes,
    Guid CorrelationId
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A split income was recorded across multiple sources.
/// TotalAmount must equal sum of Lines[].Amount (validated by handler).
/// </summary>
public sealed record SplitIncomeRecorded(
    Guid ParentId,
    Guid AccountId,
    Money TotalAmount,
    DateOnly EffectiveDate,
    IReadOnlyList<IncomeSplitLine> Lines,
    string? Notes,
    Guid CorrelationId
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable reversal event: reverses a previously recorded split expense.
/// </summary>
public sealed record SplitExpenseReversed(
    Guid ParentId,
    Guid AccountId,
    decimal TotalAmount,
    string Reason,
    DateOnly EffectiveDate,
    Guid CorrelationId
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable reversal event: reverses a previously recorded split income.
/// </summary>
public sealed record SplitIncomeReversed(
    Guid ParentId,
    Guid AccountId,
    decimal TotalAmount,
    string Reason,
    DateOnly EffectiveDate,
    Guid CorrelationId
) : DomainEvent(EffectiveDate);
