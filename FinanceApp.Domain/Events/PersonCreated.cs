namespace FinanceApp.Domain.Events;

/// <summary>
/// Immutable event: A person was registered in the system.
/// </summary>
public sealed record PersonCreated(
    Guid EventId,
    Guid PersonId,
    string FullName,
    PersonRole Role,
    ContactInfo? Contact,
    DateTimeOffset OccurredAt
) : IDomainEvent
{
    public string EventType => "PersonCreated";
}

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
/// Contact information value object.
/// </summary>
public sealed record ContactInfo(
    string? Email,
    string? Phone,
    string? Address
);