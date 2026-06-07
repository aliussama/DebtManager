using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Events;

public sealed record ObligationCreated(
    Guid ObligationId,
    string Name,
    string ObligationType,
    Money Principal,
    DateOnly StartDate,
    string CurrencyCode
) : DomainEvent(StartDate);

/// <summary>
/// Immutable event: An obligation was closed (settled, paid off, or written off).
/// The obligation is never deleted — this event marks its end state.
/// </summary>
public sealed record ObligationClosed(
    Guid ObligationId,
    DateOnly ClosureDate,
    ObligationClosureType ClosureType,
    Money FinalBalance,
    string? Reason,
    string? Notes
) : DomainEvent(ClosureDate);

/// <summary>
/// Type of obligation closure.
/// </summary>
public enum ObligationClosureType
{
    /// <summary>Fully paid off as scheduled.</summary>
    PaidInFull,
    /// <summary>Early settlement before term end.</summary>
    EarlySettlement,
    /// <summary>Refinanced into a new obligation.</summary>
    Refinanced,
    /// <summary>Consolidated with other obligations.</summary>
    Consolidated,
    /// <summary>Written off as uncollectable.</summary>
    WrittenOff,
    /// <summary>Cancelled or voided.</summary>
    Cancelled,
    /// <summary>Other closure reason.</summary>
    Other
}

/// <summary>
/// Immutable event: A charge was waived (forgiven).
/// </summary>
public sealed record ChargeWaived(
    Guid WaiverId,
    Guid ObligationId,
    Guid? InstallmentKey,
    string ChargeType,
    Money WaivedAmount,
    string Reason,
    Guid ApprovedBy,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: Obligation metadata was updated (non-financial changes only).
/// </summary>
public sealed record ObligationMetadataUpdated(
    Guid ObligationId,
    string? NewName,
    Dictionary<string, string>? NewTags,
    string? Notes,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
