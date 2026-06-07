namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: A party (vendor/customer) was created.
/// </summary>
public sealed record PartyCreated(
    Guid PartyId,
    string Kind, // "Vendor", "Customer", "Both"
    string Name,
    string DefaultCurrencyCode,
    string? ContactJson,
    string[] Tags,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A party was modified.
/// </summary>
public sealed record PartyModified(
    Guid PartyId,
    string Name,
    string DefaultCurrencyCode,
    string? ContactJson,
    string[] Tags,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A party was archived.
/// </summary>
public sealed record PartyArchived(
    Guid PartyId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
