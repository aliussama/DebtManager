namespace FinanceApp.Domain.Events;

/// <summary>
/// Immutable event: A person was linked to an obligation with a specific role.
/// This models real-world relationships: debtor, creditor, guarantor.
/// </summary>
public sealed record PersonLinkedToObligation(
    Guid EventId,
    Guid PersonId,
    Guid ObligationId,
    ObligationRole Role,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset OccurredAt
) : IDomainEvent
{
    public string EventType => "PersonLinkedToObligation";
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