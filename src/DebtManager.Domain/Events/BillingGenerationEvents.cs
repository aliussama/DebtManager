namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: A bill was generated from a contract cycle.
/// CycleKey provides idempotency (e.g. "2025-03" for monthly).
/// </summary>
public sealed record ContractBillGenerated(
    Guid ContractId,
    Guid BillId,
    string CycleKey,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: An invoice was generated from a contract cycle.
/// </summary>
public sealed record ContractInvoiceGenerated(
    Guid ContractId,
    Guid InvoiceId,
    string CycleKey,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
