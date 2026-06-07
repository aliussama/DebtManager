namespace FinanceApp.Domain.Events;

/// <summary>
/// Immutable event: A financial institution was registered.
/// Institutions define behavior (rules), not just hold money.
/// </summary>
public sealed record FinancialInstitutionRegistered(
    Guid EventId,
    Guid InstitutionId,
    string Name,
    InstitutionType Type,
    string CountryCode,
    InstitutionMetadata? Metadata,
    DateTimeOffset OccurredAt
) : IDomainEvent
{
    public string EventType => "FinancialInstitutionRegistered";
}

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