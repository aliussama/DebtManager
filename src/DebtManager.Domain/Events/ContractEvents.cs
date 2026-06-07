namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: A contract was created.
/// ContractType: "Subscription", "Rent", "Insurance", "Utilities", "Maintenance", "Custom".
/// TermsJson: JSON DSL describing billing cycle, amount rules, escalation, grace, penalties, cancellation.
/// </summary>
public sealed record ContractCreated(
    Guid ContractId,
    Guid PartyId,
    string ContractType,
    string Title,
    DateOnly StartDate,
    DateOnly? EndDate,
    string CurrencyCode,
    string TermsJson,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A contract was modified.
/// </summary>
public sealed record ContractModified(
    Guid ContractId,
    string Title,
    DateOnly? EndDate,
    string TermsJson,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A contract was archived.
/// </summary>
public sealed record ContractArchived(
    Guid ContractId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
