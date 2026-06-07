using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Quality;

namespace DebtManager.Application.UseCases;

/// <summary>
/// Use case: Compute the Financial Health Score from projection states.
/// No business logic here ? only orchestration.
/// Loads projections deterministically and delegates to FinancialHealthCalculator.
/// </summary>
public sealed class GetFinancialHealthHandler
{
    private readonly IEventStore _store;

    public GetFinancialHealthHandler(IEventStore store)
    {
        _store = store;
    }

    public async Task<HealthScore> HandleAsync(DateOnly asOfDate, int evaluationMonths = 3, CancellationToken ct = default)
    {
        var allEnvelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);

        // Project all required states deterministically
        var cashState = CashLedgerProjector.Project(allEnvelopes);
        var budgetState = BudgetProjector.Project(allEnvelopes);
        var goalsState = GoalsProjector.Project(allEnvelopes, asOfDate);
        var billingState = BillingProjector.Project(allEnvelopes, asOfDate);
        var incomeSourceState = IncomeSourceProjector.Project(allEnvelopes);

        // Delegate to domain calculator
        var calculator = new FinancialHealthCalculator();
        return calculator.Compute(
            cashState,
            budgetState,
            goalsState,
            billingState,
            incomeSourceState,
            asOfDate,
            evaluationMonths);
    }
}
