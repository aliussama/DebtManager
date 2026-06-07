using DebtManager.Domain.Projections;

namespace DebtManager.Domain.Quality;

/// <summary>
/// Pure function: Computes a deterministic financial health score (0-100) from projection states.
/// No randomness. No DateTime.Now. No static state.
/// Handles zero-income and zero-expense edge cases safely.
/// Multi-vault isolation preserved (operates only on injected states).
/// </summary>
public sealed class FinancialHealthCalculator
{
    private const decimal WeightDebtToIncome = 0.25m;
    private const decimal WeightSavingsRate = 0.20m;
    private const decimal WeightLiquidity = 0.20m;
    private const decimal WeightEmergencyFund = 0.15m;
    private const decimal WeightBudgetAdherence = 0.10m;
    private const decimal WeightOverdueRatio = 0.10m;

    /// <summary>
    /// Computes health score from projection states.
    /// All inputs are projection states loaded deterministically.
    /// Returns HealthScore with grade and component breakdown.
    /// </summary>
    public HealthScore Compute(
        CashLedgerState cashState,
        BudgetState budgetState,
        GoalsState goalsState,
        BillingState billingState,
        IncomeSourceState incomeSourceState,
        DateOnly asOfDate,
        int evaluationMonths = 3)
    {
        var components = new List<HealthComponent>();

        // 1. Debt-to-Income Ratio (lower is better)
        var debtToIncome = ComputeDebtToIncomeRatio(cashState, billingState, asOfDate, evaluationMonths);
        var debtToIncomeScore = ScoreDebtToIncome(debtToIncome);
        components.Add(new HealthComponent(
            "Debt-to-Income Ratio",
            debtToIncome,
            WeightDebtToIncome,
            StatusFromScore(debtToIncomeScore)));

        // 2. Savings Rate (higher is better)
        var savingsRate = ComputeSavingsRate(cashState, asOfDate, evaluationMonths);
        var savingsRateScore = ScoreSavingsRate(savingsRate);
        components.Add(new HealthComponent(
            "Savings Rate",
            savingsRate,
            WeightSavingsRate,
            StatusFromScore(savingsRateScore)));

        // 3. Liquidity Ratio (liquid assets / monthly expenses)
        var liquidityRatio = ComputeLiquidityRatio(cashState, asOfDate, evaluationMonths);
        var liquidityScore = ScoreLiquidityRatio(liquidityRatio);
        components.Add(new HealthComponent(
            "Liquidity Ratio",
            liquidityRatio,
            WeightLiquidity,
            StatusFromScore(liquidityScore)));

        // 4. Emergency Fund Months (how many months of expenses are covered)
        var emergencyMonths = ComputeEmergencyFundMonths(cashState, asOfDate, evaluationMonths);
        var emergencyScore = ScoreEmergencyFundMonths(emergencyMonths);
        components.Add(new HealthComponent(
            "Emergency Fund Months",
            emergencyMonths,
            WeightEmergencyFund,
            StatusFromScore(emergencyScore)));

        // 5. Budget Adherence (percent of budgets within limit)
        var budgetAdherence = ComputeBudgetAdherence(budgetState, cashState, asOfDate);
        var budgetScore = ScoreBudgetAdherence(budgetAdherence);
        components.Add(new HealthComponent(
            "Budget Adherence",
            budgetAdherence,
            WeightBudgetAdherence,
            StatusFromScore(budgetScore)));

        // 6. Overdue Ratio (percent of bills/invoices that are overdue)
        var overdueRatio = ComputeOverdueRatio(billingState, asOfDate);
        var overdueScore = ScoreOverdueRatio(overdueRatio);
        components.Add(new HealthComponent(
            "Overdue Ratio",
            overdueRatio,
            WeightOverdueRatio,
            StatusFromScore(overdueScore)));

        // Weighted composite score
        var totalWeightedScore = components.Sum(c =>
        {
            var componentScore = c.Name switch
            {
                "Debt-to-Income Ratio" => debtToIncomeScore,
                "Savings Rate" => savingsRateScore,
                "Liquidity Ratio" => liquidityScore,
                "Emergency Fund Months" => emergencyScore,
                "Budget Adherence" => budgetScore,
                "Overdue Ratio" => overdueScore,
                _ => 0m
            };
            return componentScore * c.Weight;
        });

        var finalScore = (int)Math.Clamp(Math.Round(totalWeightedScore, 0, MidpointRounding.AwayFromZero), 0, 100);
        var grade = ComputeGrade(finalScore);

        return new HealthScore(finalScore, grade, components);
    }

    // ================================================================
    // Component Computations (Pure Functions)
    // ================================================================

    /// <summary>
    /// Debt-to-Income Ratio = Total Debt Payments / Monthly Income.
    /// Lower is better. 0 = no debt. >1 = debt exceeds income.
    /// </summary>
    private static decimal ComputeDebtToIncomeRatio(
        CashLedgerState cashState,
        BillingState billingState,
        DateOnly asOfDate,
        int evaluationMonths)
    {
        var startDate = asOfDate.AddMonths(-evaluationMonths);
        var income = cashState.Rows
            .Where(r => r.Direction == "In" && r.EffectiveDate >= startDate && r.EffectiveDate <= asOfDate)
            .Sum(r => r.Amount);

        if (income <= 0) return 0m;

        var monthlyIncome = income / evaluationMonths;

        // Debt payments = bills paid + invoices paid in the period
        var debtPayments = cashState.Rows
            .Where(r => r.Direction == "Out"
                        && r.EffectiveDate >= startDate
                        && r.EffectiveDate <= asOfDate
                        && (r.Category.Contains("Debt", StringComparison.OrdinalIgnoreCase)
                            || r.Category.Contains("Loan", StringComparison.OrdinalIgnoreCase)
                            || r.Category.Contains("Bill", StringComparison.OrdinalIgnoreCase)))
            .Sum(r => r.Amount);

        var monthlyDebtPayment = debtPayments / evaluationMonths;

        return monthlyIncome > 0 ? Math.Round(monthlyDebtPayment / monthlyIncome, 2, MidpointRounding.AwayFromZero) : 0m;
    }

    /// <summary>
    /// Savings Rate = (Income - Expenses) / Income.
    /// Higher is better. 0 = no savings. Negative = spending more than earning.
    /// </summary>
    private static decimal ComputeSavingsRate(
        CashLedgerState cashState,
        DateOnly asOfDate,
        int evaluationMonths)
    {
        var startDate = asOfDate.AddMonths(-evaluationMonths);
        var income = cashState.Rows
            .Where(r => r.Direction == "In" && r.EffectiveDate >= startDate && r.EffectiveDate <= asOfDate)
            .Sum(r => r.Amount);

        var expenses = cashState.Rows
            .Where(r => r.Direction == "Out" && r.EffectiveDate >= startDate && r.EffectiveDate <= asOfDate)
            .Sum(r => r.Amount);

        if (income <= 0) return 0m;

        var savings = income - expenses;
        return Math.Round(savings / income, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Liquidity Ratio = Liquid Assets / Monthly Expenses.
    /// Higher is better. Shows how many months of expenses are covered by liquid assets.
    /// </summary>
    private static decimal ComputeLiquidityRatio(
        CashLedgerState cashState,
        DateOnly asOfDate,
        int evaluationMonths)
    {
        var liquidAssets = cashState.Accounts.Values
            .Where(a => !a.IsArchived)
            .Sum(a => a.Balance);

        if (liquidAssets <= 0) return 0m;

        var startDate = asOfDate.AddMonths(-evaluationMonths);
        var expenses = cashState.Rows
            .Where(r => r.Direction == "Out" && r.EffectiveDate >= startDate && r.EffectiveDate <= asOfDate)
            .Sum(r => r.Amount);

        var monthlyExpenses = expenses / evaluationMonths;

        return monthlyExpenses > 0 ? Math.Round(liquidAssets / monthlyExpenses, 2, MidpointRounding.AwayFromZero) : 0m;
    }

    /// <summary>
    /// Emergency Fund Months = Cash Balance / Monthly Expenses.
    /// Higher is better. 6+ months is ideal.
    /// </summary>
    private static decimal ComputeEmergencyFundMonths(
        CashLedgerState cashState,
        DateOnly asOfDate,
        int evaluationMonths)
    {
        var cashBalance = cashState.Accounts.Values
            .Where(a => !a.IsArchived)
            .Sum(a => a.Balance);

        if (cashBalance <= 0) return 0m;

        var startDate = asOfDate.AddMonths(-evaluationMonths);
        var expenses = cashState.Rows
            .Where(r => r.Direction == "Out" && r.EffectiveDate >= startDate && r.EffectiveDate <= asOfDate)
            .Sum(r => r.Amount);

        var monthlyExpenses = expenses / evaluationMonths;

        return monthlyExpenses > 0 ? Math.Round(cashBalance / monthlyExpenses, 1, MidpointRounding.AwayFromZero) : 0m;
    }

    /// <summary>
    /// Budget Adherence = Percent of budgets within limit.
    /// 100% = all budgets OK. 0% = all budgets exceeded.
    /// </summary>
    private static decimal ComputeBudgetAdherence(
        BudgetState budgetState,
        CashLedgerState cashState,
        DateOnly asOfDate)
    {
        var currentBudgets = budgetState.Budgets.Values
            .Where(b => !b.IsArchived && b.PeriodYear == asOfDate.Year && b.PeriodMonth == asOfDate.Month)
            .ToList();

        if (currentBudgets.Count == 0)
            return 100m;

        var withinLimit = currentBudgets.Count(b =>
        {
            var spent = cashState.Rows
                .Where(r => r.Direction == "Out"
                            && r.EffectiveDate.Year == asOfDate.Year
                            && r.EffectiveDate.Month == asOfDate.Month
                            && (b.ScopeType == "category" && r.Category == GetCategoryName(b.CategoryId))
                            || (b.ScopeType == "account" && r.AccountId == b.AccountId))
                .Sum(r => r.Amount);

            return spent <= b.LimitAmount;
        });

        return Math.Round((decimal)withinLimit / currentBudgets.Count * 100m, 1, MidpointRounding.AwayFromZero);

        static string GetCategoryName(Guid? categoryId)
        {
            return categoryId?.ToString() ?? "Uncategorized";
        }
    }

    /// <summary>
    /// Overdue Ratio = Overdue Bills / Total Bills.
    /// Lower is better. 0 = no overdue payments.
    /// </summary>
    private static decimal ComputeOverdueRatio(
        BillingState billingState,
        DateOnly asOfDate)
    {
        var totalBills = billingState.Bills.Values
            .Count(b => !b.IsCancelled && !b.IsWrittenOff);
        var totalInvoices = billingState.Invoices.Values
            .Count(i => !i.IsCancelled && !i.IsWrittenOff);
        var totalObligations = totalBills + totalInvoices;

        if (totalObligations == 0)
            return 0m;

        var overdueBills = billingState.OverdueBills(asOfDate).Count;
        var overdueInvoices = billingState.OverdueInvoices(asOfDate).Count;
        var totalOverdue = overdueBills + overdueInvoices;

        return Math.Round((decimal)totalOverdue / totalObligations, 2, MidpointRounding.AwayFromZero);
    }

    // ================================================================
    // Scoring Functions (Component Value -> 0-100 Score)
    // ================================================================

    private static decimal ScoreDebtToIncome(decimal ratio)
    {
        // 0.0 = 100, 0.3 = 75, 0.5 = 50, 0.7 = 25, 1.0+ = 0
        if (ratio <= 0) return 100m;
        if (ratio >= 1.0m) return 0m;
        if (ratio <= 0.3m) return 100m - (ratio / 0.3m * 25m);
        if (ratio <= 0.5m) return 75m - ((ratio - 0.3m) / 0.2m * 25m);
        if (ratio <= 0.7m) return 50m - ((ratio - 0.5m) / 0.2m * 25m);
        return 25m - ((ratio - 0.7m) / 0.3m * 25m);
    }

    private static decimal ScoreSavingsRate(decimal rate)
    {
        // -0.2 = 0, 0 = 40, 0.1 = 70, 0.2 = 85, 0.3+ = 100
        if (rate <= -0.2m) return 0m;
        if (rate <= 0) return 40m + (rate + 0.2m) / 0.2m * 40m;
        if (rate <= 0.1m) return 40m + rate / 0.1m * 30m;
        if (rate <= 0.2m) return 70m + (rate - 0.1m) / 0.1m * 15m;
        if (rate <= 0.3m) return 85m + (rate - 0.2m) / 0.1m * 15m;
        return 100m;
    }

    private static decimal ScoreLiquidityRatio(decimal ratio)
    {
        // 0 = 0, 1 = 40, 3 = 70, 6 = 90, 12+ = 100
        if (ratio <= 0) return 0m;
        if (ratio <= 1m) return ratio / 1m * 40m;
        if (ratio <= 3m) return 40m + (ratio - 1m) / 2m * 30m;
        if (ratio <= 6m) return 70m + (ratio - 3m) / 3m * 20m;
        if (ratio <= 12m) return 90m + (ratio - 6m) / 6m * 10m;
        return 100m;
    }

    private static decimal ScoreEmergencyFundMonths(decimal months)
    {
        // 0 = 0, 1 = 30, 3 = 60, 6 = 90, 12+ = 100
        if (months <= 0) return 0m;
        if (months <= 1m) return months / 1m * 30m;
        if (months <= 3m) return 30m + (months - 1m) / 2m * 30m;
        if (months <= 6m) return 60m + (months - 3m) / 3m * 30m;
        if (months <= 12m) return 90m + (months - 6m) / 6m * 10m;
        return 100m;
    }

    private static decimal ScoreBudgetAdherence(decimal percent)
    {
        // 0% = 0, 50% = 40, 75% = 70, 90% = 90, 100% = 100
        if (percent <= 0) return 0m;
        if (percent <= 50m) return percent / 50m * 40m;
        if (percent <= 75m) return 40m + (percent - 50m) / 25m * 30m;
        if (percent <= 90m) return 70m + (percent - 75m) / 15m * 20m;
        return 90m + (percent - 90m) / 10m * 10m;
    }

    private static decimal ScoreOverdueRatio(decimal ratio)
    {
        // 0 = 100, 0.1 = 70, 0.25 = 40, 0.5+ = 0
        if (ratio <= 0) return 100m;
        if (ratio >= 0.5m) return 0m;
        if (ratio <= 0.1m) return 100m - ratio / 0.1m * 30m;
        if (ratio <= 0.25m) return 70m - (ratio - 0.1m) / 0.15m * 30m;
        return 40m - (ratio - 0.25m) / 0.25m * 40m;
    }

    // ================================================================
    // Grade Assignment
    // ================================================================

    private static string ComputeGrade(int score)
    {
        if (score >= 90) return "A";
        if (score >= 75) return "B";
        if (score >= 60) return "C";
        if (score >= 40) return "D";
        return "F";
    }

    private static string StatusFromScore(decimal score)
    {
        if (score >= 90) return "Excellent";
        if (score >= 75) return "Good";
        if (score >= 60) return "Fair";
        if (score >= 40) return "Poor";
        return "Critical";
    }
}
