namespace DebtManager.Application.Simulation;

public enum HypothesisType
{
    ExtraPayment,
    DelayedPayment,
    MissPayment,
    OneTimeExpense,
    IncomeShock
}

public sealed record Hypothesis(
    HypothesisType Type,
    DateOnly EffectiveDate,

    // Money parameters (optional, used by many hypotheses)
    decimal? Amount = null,
    string? CurrencyCode = null,

    // For delayed payment
    DateOnly? NewEffectiveDate = null,
    string? PaymentReferenceContains = null,

    // For income shock
    decimal? IncomeDelta = null,         // negative means income drop
    string? IncomeSourceContains = null, // optional filter

    // Free text
    string? Reference = null
);

public sealed record SimulateScenarioCommand(
    Guid ObligationId,
    DateOnly AsOfDate,
    DateOnly HorizonEndDate,
    IReadOnlyList<Hypothesis> Hypotheses
);

public sealed record ScenarioDiff(
    decimal BaselineTotalPayments,
    decimal ScenarioTotalPayments,
    int BaselineChargesCount,
    int ScenarioChargesCount,
    int BaselineOverdueInstallments,
    int ScenarioOverdueInstallments
);

public sealed record ScenarioResult(
    DebtManager.Domain.Projections.FinancialState Baseline,
    DebtManager.Domain.Projections.FinancialState Scenario,
    ScenarioDiff Diff
);
