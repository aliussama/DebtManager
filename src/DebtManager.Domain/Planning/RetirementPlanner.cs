using DebtManager.Domain.Projections;

namespace DebtManager.Domain.Planning;

/// <summary>
/// Deterministic retirement planning result.
/// </summary>
public sealed record RetirementPlanResult(
    string ReportingCurrencyCode,
    DateOnly AsOfDate,
    DateOnly RetirementDate,
    decimal YearsToRetirement,
    decimal CurrentNetWorthKnown,
    int UnknownValueCount,
    decimal RequiredCorpusAtRetirement,
    decimal ProjectedCorpusAtRetirement,
    decimal FundingGap,
    decimal RequiredMonthlySavings,
    decimal MonthlySpendingAtRetirementInflationAdjusted,
    decimal SafeWithdrawalRate,
    decimal ReturnRate,
    decimal InflationRate,
    IReadOnlyList<string> Warnings
);

/// <summary>
/// Pure function engine for retirement planning. No I/O.
/// Uses annuity-due formula (contributions at start of month).
/// </summary>
public static class RetirementPlanner
{
    public static RetirementPlanResult Compute(
        RetirementProfileRecord profile,
        RetirementAssumptionsRecord assumptions,
        decimal currentNetWorthKnown,
        int unknownValueCount,
        DateOnly asOfDate)
    {
        var warnings = new List<string>();

        if (unknownValueCount > 0)
            warnings.Add($"{unknownValueCount} asset(s) have unknown values and are excluded from net worth.");

        var yearsToRetirement = (profile.RetirementDate.DayNumber - asOfDate.DayNumber) / 365.25m;
        if (yearsToRetirement < 0)
        {
            yearsToRetirement = 0;
            warnings.Add("Retirement date is in the past.");
        }

        var monthlySpendingToday = profile.DesiredMonthlySpending.Amount;
        var inflationRate = assumptions.ExpectedAnnualInflation;
        var returnRate = assumptions.ExpectedAnnualReturnRate;
        var swr = profile.SafeWithdrawalRate;
        var monthlySavings = assumptions.CurrentMonthlySavings.Amount;

        // Inflate monthly spending to retirement date
        var inflatedMonthly = monthlySpendingToday * Pow(1m + inflationRate, yearsToRetirement);
        inflatedMonthly = Math.Round(inflatedMonthly, 2, MidpointRounding.AwayFromZero);

        // Required corpus = annual spending / SWR
        var annualSpendingAtRetirement = inflatedMonthly * 12m;
        var requiredCorpus = swr > 0 ? annualSpendingAtRetirement / swr : 0m;
        requiredCorpus = Math.Round(requiredCorpus, 2, MidpointRounding.AwayFromZero);

        if (swr <= 0)
            warnings.Add("Safe withdrawal rate is zero; cannot compute required corpus.");

        // Projected corpus = FV of current net worth + FV of monthly contributions (annuity due)
        var projectedFromNetWorth = currentNetWorthKnown * Pow(1m + returnRate, yearsToRetirement);

        var totalMonths = (int)Math.Floor(yearsToRetirement * 12m);
        var fvContributions = FutureValueAnnuityDue(monthlySavings, returnRate / 12m, totalMonths);

        var projectedCorpus = Math.Round(projectedFromNetWorth + fvContributions, 2, MidpointRounding.AwayFromZero);

        // Funding gap (positive = shortfall)
        var gap = Math.Round(requiredCorpus - projectedCorpus, 2, MidpointRounding.AwayFromZero);

        // Required monthly savings to close gap
        var requiredMonthlySavings = SolveRequiredMonthlyPayment(
            requiredCorpus, currentNetWorthKnown, returnRate, yearsToRetirement);
        requiredMonthlySavings = Math.Round(requiredMonthlySavings, 2, MidpointRounding.AwayFromZero);

        if (requiredMonthlySavings < 0)
            requiredMonthlySavings = 0; // Already on track

        return new RetirementPlanResult(
            assumptions.ReportingCurrencyCode,
            asOfDate,
            profile.RetirementDate,
            Math.Round(yearsToRetirement, 2, MidpointRounding.AwayFromZero),
            currentNetWorthKnown,
            unknownValueCount,
            requiredCorpus,
            projectedCorpus,
            gap,
            requiredMonthlySavings,
            inflatedMonthly,
            swr,
            returnRate,
            inflationRate,
            warnings);
    }

    /// <summary>
    /// Future value of an annuity due: PMT * [((1+r)^n - 1) / r] * (1+r).
    /// If r == 0, returns PMT * n.
    /// </summary>
    internal static decimal FutureValueAnnuityDue(decimal pmt, decimal monthlyRate, int totalMonths)
    {
        if (totalMonths <= 0) return 0m;
        if (monthlyRate == 0) return pmt * totalMonths;

        var factor = Pow(1m + monthlyRate, totalMonths) - 1m;
        return pmt * (factor / monthlyRate) * (1m + monthlyRate);
    }

    /// <summary>
    /// Solve for the monthly payment (annuity due) required so that
    /// FV(currentNW) + FV(PMT stream) = targetCorpus.
    /// </summary>
    internal static decimal SolveRequiredMonthlyPayment(
        decimal targetCorpus, decimal currentNW, decimal annualReturn, decimal yearsToRetirement)
    {
        if (yearsToRetirement <= 0) return 0m;

        var fvNW = currentNW * Pow(1m + annualReturn, yearsToRetirement);
        var deficit = targetCorpus - fvNW;

        if (deficit <= 0) return 0m; // No gap

        var totalMonths = (int)Math.Floor(yearsToRetirement * 12m);
        if (totalMonths <= 0) return 0m;

        var monthlyRate = annualReturn / 12m;
        if (monthlyRate == 0) return deficit / totalMonths;

        // FV annuity due factor = [((1+r)^n - 1) / r] * (1+r)
        var factor = (Pow(1m + monthlyRate, totalMonths) - 1m) / monthlyRate * (1m + monthlyRate);
        return factor > 0 ? deficit / factor : 0m;
    }

    /// <summary>
    /// Decimal exponentiation for compound growth: (1+r)^n using double precision.
    /// </summary>
    private static decimal Pow(decimal baseVal, decimal exponent)
    {
        if (exponent == 0) return 1m;
        return (decimal)Math.Pow((double)baseVal, (double)exponent);
    }

    // Overload for int exponent
    private static decimal Pow(decimal baseVal, int exponent)
    {
        if (exponent == 0) return 1m;
        return (decimal)Math.Pow((double)baseVal, exponent);
    }
}
