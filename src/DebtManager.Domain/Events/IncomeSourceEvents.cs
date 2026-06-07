namespace DebtManager.Domain.Events;

/// <summary>
/// Income source types for first-class classification.
/// </summary>
public enum IncomeSourceType
{
    Salary,
    Freelance,
    Rental,
    Dividends,
    Interest,
    Business,
    Government,
    Gift,
    Other
}

/// <summary>
/// Immutable event: An income source entity was defined.
/// </summary>
public sealed record IncomeSourceDefined(
    Guid SourceId,
    string Name,
    IncomeSourceType SourceType,
    string CurrencyCode,
    bool IsRecurring,
    DateOnly EffectiveDate,
    string? Notes
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: An income source was archived.
/// </summary>
public sealed record IncomeSourceArchived(
    Guid SourceId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
