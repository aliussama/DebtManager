namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: An adjustment was added to a bill.
/// Kind: "Discount", "Fee", "Tax", "Credit", "Debit", "Other".
/// </summary>
public sealed record BillAdjustmentAdded(
    Guid BillId,
    Guid AdjustmentId,
    string Kind,
    decimal Amount,
    string? Notes,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: An adjustment was added to an invoice.
/// </summary>
public sealed record InvoiceAdjustmentAdded(
    Guid InvoiceId,
    Guid AdjustmentId,
    string Kind,
    decimal Amount,
    string? Notes,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A bill adjustment was reversed.
/// </summary>
public sealed record BillAdjustmentReversed(
    Guid BillId,
    Guid AdjustmentId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: An invoice adjustment was reversed.
/// </summary>
public sealed record InvoiceAdjustmentReversed(
    Guid InvoiceId,
    Guid AdjustmentId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
