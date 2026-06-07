namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: A bill (accounts payable) was issued.
/// </summary>
public sealed record BillIssued(
    Guid BillId,
    Guid? ContractId,
    Guid PartyId,
    string CurrencyCode,
    decimal Amount,
    DateOnly DueDate,
    string Category,
    string Reference,
    string? Notes,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: An invoice (accounts receivable) was issued.
/// </summary>
public sealed record InvoiceIssued(
    Guid InvoiceId,
    Guid? ContractId,
    Guid PartyId,
    string CurrencyCode,
    decimal Amount,
    DateOnly DueDate,
    string Category,
    string Reference,
    string? Notes,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A bill was cancelled.
/// </summary>
public sealed record BillCancelled(
    Guid BillId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: An invoice was cancelled.
/// </summary>
public sealed record InvoiceCancelled(
    Guid InvoiceId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A bill was disputed.
/// </summary>
public sealed record BillDisputed(
    Guid BillId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: An invoice was disputed.
/// </summary>
public sealed record InvoiceDisputed(
    Guid InvoiceId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A bill was written off.
/// </summary>
public sealed record BillWrittenOff(
    Guid BillId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: An invoice was written off.
/// </summary>
public sealed record InvoiceWrittenOff(
    Guid InvoiceId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
