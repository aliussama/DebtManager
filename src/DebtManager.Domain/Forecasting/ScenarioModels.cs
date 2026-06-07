namespace DebtManager.Domain.Forecasting;

public enum ScenarioChangeKind
{
    OneTimeIncome,
    OneTimeExpense,
    OneTimeTransfer,
    BudgetOverride,
    RecurringOverride,
    DebtExtraPayment,
    PauseRecurring,
    FxPolicyOverride,
    ReportingCurrencyOverride
}

public sealed record ScenarioChange(
    Guid ChangeId,
    ScenarioChangeKind Kind,
    string PayloadJson
);

public sealed record ScenarioDefinition(
    Guid ScenarioId,
    string Name,
    string Notes,
    ForecastHorizon Horizon,
    IReadOnlyList<ScenarioChange> Changes
);

/// <summary>
/// A normalized adjustment entry produced by the scenario applier for the forecast engine.
/// </summary>
public sealed record ForecastAdjustment(
    DateOnly Date,
    Guid? AccountId,
    string CurrencyCode,
    decimal Amount,
    string Kind, // "Income", "Expense", "Transfer", "DebtPayment"
    string Category,
    string Reference,
    Guid? RecurringIdToSuppress // If set, suppresses this recurring's normal expansion
);
