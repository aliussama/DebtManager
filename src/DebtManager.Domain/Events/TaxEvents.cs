namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: A tax profile was created.
/// </summary>
public sealed record TaxProfileCreated(
    Guid ProfileId,
    string Name,
    string CountryCode,
    int TaxYearStartMonth,
    int TaxYearStartDay,
    string BaseCurrencyCode,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A tax profile was modified.
/// Null fields mean "no change".
/// </summary>
public sealed record TaxProfileModified(
    Guid ProfileId,
    string? Name,
    string? CountryCode,
    int? TaxYearStartMonth,
    int? TaxYearStartDay,
    string? BaseCurrencyCode,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A tax profile was archived.
/// </summary>
public sealed record TaxProfileArchived(
    Guid ProfileId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A user explicitly confirmed the tax classification for a specific source item.
/// Highest priority in the classification hierarchy.
/// </summary>
public sealed record TaxConfirmClassification(
    Guid ClassificationId,
    string SourceType,
    string SourceId,
    string TaxCategory,
    string Notes,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A pattern-based tax rule was defined.
/// AppliesTo: "ExpenseCategory" | "IncomeSource" | "Symbol" | "TransactionType"
/// </summary>
public sealed record TaxRuleDefined(
    Guid RuleId,
    string AppliesTo,
    string MatchValue,
    string TaxCategory,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A tax rule was archived.
/// </summary>
public sealed record TaxRuleArchived(
    Guid RuleId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);
