namespace DebtManager.Domain.Events;

/// <summary>
/// Role a person plays in the financial domain.
/// A person may have multiple roles across different obligations.
/// </summary>
public enum PersonRole
{
    /// <summary>The individual whose finances are managed (self).</summary>
    Self,
    /// <summary>Someone who owes money (debtor on an obligation).</summary>
    Debtor,
    /// <summary>Someone who is owed money.</summary>
    Creditor,
    /// <summary>Someone who guarantees another's obligation.</summary>
    Guarantor,
    /// <summary>A dependent whose finances are tracked (child, spouse).</summary>
    Dependent
}

/// <summary>
/// The role a person plays on a specific obligation.
/// </summary>
public enum ObligationRole
{
    /// <summary>Primary debtor - responsible for payment.</summary>
    PrimaryDebtor,
    /// <summary>Co-debtor - jointly responsible.</summary>
    CoDebtor,
    /// <summary>Guarantor - liable if debtor defaults.</summary>
    Guarantor,
    /// <summary>Creditor - the party owed money.</summary>
    Creditor
}

/// <summary>
/// Contact information value object.
/// </summary>
public sealed record ContactInfo(
    string? Email,
    string? Phone,
    string? Address
);

/// <summary>
/// Immutable event: A person was registered in the system.
/// </summary>
public sealed record PersonCreated(
    Guid PersonId,
    string FullName,
    PersonRole PrimaryRole,
    ContactInfo? Contact,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A person was linked to an obligation with a specific role.
/// This models real-world relationships: debtor, creditor, guarantor.
/// </summary>
public sealed record PersonLinkedToObligation(
    Guid PersonId,
    Guid ObligationId,
    ObligationRole Role,
    DateOnly EffectiveFrom
) : DomainEvent(EffectiveFrom);

/// <summary>
/// Immutable event: Person contact info was updated.
/// </summary>
public sealed record PersonContactUpdated(
    Guid PersonId,
    ContactInfo NewContact,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
