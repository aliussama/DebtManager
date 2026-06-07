namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: Initial setup wizard was completed.
/// </summary>
public sealed record InitialSetupCompleted(
    DateOnly EffectiveDate,
    Guid SetupId,
    string ReportingCurrencyCode,
    int FiscalYearStartMonth,
    bool CreatedDefaultAccounts,
    bool CreatedDefaultCategories,
    bool SeededDemoData
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: Demo data was seeded into the event store.
/// </summary>
public sealed record DemoDataSeeded(
    DateOnly EffectiveDate,
    Guid DemoSeedId,
    string SeedVersion,
    string Notes
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: Demo data was cleared (archived/reversed).
/// </summary>
public sealed record DemoDataCleared(
    DateOnly EffectiveDate,
    Guid DemoSeedId,
    string Reason
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: Default accounts were created during setup.
/// </summary>
public sealed record DefaultAccountsCreated(
    DateOnly EffectiveDate,
    Guid SetupId,
    string CurrencyCode
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: Default categories were created during setup.
/// </summary>
public sealed record DefaultCategoriesCreated(
    DateOnly EffectiveDate,
    Guid SetupId
) : DomainEvent(EffectiveDate);
