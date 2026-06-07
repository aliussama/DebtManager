namespace FinanceApp.Domain.Events;

/// <summary>
/// Immutable event: An obligation was linked to a financial institution.
/// This determines which rule packs apply.
/// </summary>
public sealed record ObligationLinkedToInstitution(
    Guid EventId,
    Guid ObligationId,
    Guid InstitutionId,
    string? ProductCode,
    string? ContractReference,
    DateTimeOffset OccurredAt
) : IDomainEvent
{
    public string EventType => "ObligationLinkedToInstitution";
}