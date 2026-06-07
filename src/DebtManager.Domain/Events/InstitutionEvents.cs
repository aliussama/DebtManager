namespace DebtManager.Domain.Events;

/// <summary>
/// Type of financial institution.
/// </summary>
public enum InstitutionType
{
    Bank,
    University,
    PropertyDeveloper,
    CreditCardIssuer,
    Government,
    Utility,
    Telecom,
    Insurance,
    Other
}

/// <summary>
/// Additional institution metadata.
/// </summary>
public sealed record InstitutionMetadata(
    string? BranchCode,
    string? SwiftCode,
    string? Website,
    string? SupportPhone,
    Dictionary<string, string>? CustomFields
);

/// <summary>
/// Immutable event: A financial institution was registered.
/// Institutions don't just hold money — they define behavior (rules).
/// </summary>
public sealed record FinancialInstitutionRegistered(
    Guid InstitutionId,
    string Name,
    InstitutionType Type,
    string CountryCode,
    InstitutionMetadata? Metadata,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: An obligation was linked to a financial institution.
/// This determines which rule packs apply.
/// </summary>
public sealed record ObligationLinkedToInstitution(
    Guid ObligationId,
    Guid InstitutionId,
    string? ProductCode,
    string? ContractReference,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: Institution metadata was updated.
/// </summary>
public sealed record InstitutionMetadataUpdated(
    Guid InstitutionId,
    InstitutionMetadata NewMetadata,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
