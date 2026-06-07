namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: A payment was recorded against a bill (cash out).
/// </summary>
public sealed record BillPaymentRecorded(
    Guid BillId,
    Guid PaymentId,
    Guid AccountId,
    decimal Amount,
    string CurrencyCode,
    DateOnly PaidDate,
    string Method,
    string? ExternalReference,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A payment was recorded against an invoice (cash in).
/// </summary>
public sealed record InvoicePaymentRecorded(
    Guid InvoiceId,
    Guid PaymentId,
    Guid AccountId,
    decimal Amount,
    string CurrencyCode,
    DateOnly PaidDate,
    string Method,
    string? ExternalReference,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A bill payment was reversed.
/// </summary>
public sealed record BillPaymentReversed(
    Guid BillId,
    Guid PaymentId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: An invoice payment was reversed.
/// </summary>
public sealed record InvoicePaymentReversed(
    Guid InvoiceId,
    Guid PaymentId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A bill payment was unapplied (detached from bill but payment record remains).
/// </summary>
public sealed record BillPaymentUnapplied(
    Guid BillId,
    Guid PaymentId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: An invoice payment was unapplied.
/// </summary>
public sealed record InvoicePaymentUnapplied(
    Guid InvoiceId,
    Guid PaymentId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
