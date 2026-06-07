namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: A monthly budget was defined.
/// ScopeType: "category", "account", or "category+account".
/// CarryPolicy: "None", "CarryUnused", "CarryOverspend".
/// </summary>
public sealed record BudgetDefined(
    Guid BudgetId,
    int PeriodYear,
    int PeriodMonth,
    string CurrencyCode,
    string ScopeType,
    Guid? CategoryId,
    Guid? AccountId,
    decimal LimitAmount,
    string CarryPolicy,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A budget limit was adjusted.
/// </summary>
public sealed record BudgetAdjusted(
    Guid BudgetId,
    decimal NewLimitAmount,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A budget was archived.
/// </summary>
public sealed record BudgetArchived(
    Guid BudgetId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);
