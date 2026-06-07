using DebtManager.Domain.Services.Finance;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Services.Rules;

/// <summary>
/// Rule for interest accrual logic.
/// Supports simple/compound interest with various day count conventions.
/// Rate schedules support intro rates, step-up, and variable rates.
/// </summary>
public sealed class InterestAccrualRule
{
    public string RuleKey { get; }
    public IReadOnlyList<RateScheduleEntry> RateSchedule { get; }
    public Compounding CompoundingMethod { get; }
    public DayCountBasis DayCountBasis { get; }
    public AccrualTrigger Trigger { get; }
    public int? GraceDaysBeforeAccrual { get; }

    public InterestAccrualRule(
        string ruleKey,
        IReadOnlyList<RateScheduleEntry> rateSchedule,
        Compounding compoundingMethod = Compounding.Daily,
        DayCountBasis dayCountBasis = DayCountBasis.Actual365,
        AccrualTrigger trigger = AccrualTrigger.OnOverdue,
        int? graceDaysBeforeAccrual = null)
    {
        if (rateSchedule == null || !rateSchedule.Any())
            throw new ArgumentException("At least one rate schedule entry is required.", nameof(rateSchedule));

        RuleKey = ruleKey;
        RateSchedule = rateSchedule;
        CompoundingMethod = compoundingMethod;
        DayCountBasis = dayCountBasis;
        Trigger = trigger;
        GraceDaysBeforeAccrual = graceDaysBeforeAccrual;
    }

    /// <summary>
    /// Calculate interest for a period.
    /// </summary>
    public InterestAccrualResult Calculate(
        Money principal,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly? dueDate = null)
    {
        if (periodEnd <= periodStart)
        {
            return new InterestAccrualResult(
                Interest: Money.Zero(principal.Currency),
                DaysAccrued: 0,
                EffectiveRate: 0m,
                Formula: "No accrual period",
                Breakdown: Array.Empty<DailyAccrualEntry>(),
                RuleKey: RuleKey
            );
        }

        // Check grace period before accrual
        if (GraceDaysBeforeAccrual.HasValue && dueDate.HasValue)
        {
            var graceExpiry = dueDate.Value.AddDays(GraceDaysBeforeAccrual.Value);
            if (periodEnd <= graceExpiry)
            {
                return new InterestAccrualResult(
                    Interest: Money.Zero(principal.Currency),
                    DaysAccrued: 0,
                    EffectiveRate: 0m,
                    Formula: $"Within grace period ({GraceDaysBeforeAccrual} days)",
                    Breakdown: Array.Empty<DailyAccrualEntry>(),
                    RuleKey: RuleKey
                );
            }

            // Adjust start to after grace period
            if (periodStart < graceExpiry)
            {
                periodStart = graceExpiry;
            }
        }

        var breakdown = new List<DailyAccrualEntry>();
        var totalInterest = 0m;
        var currentPrincipal = principal.Amount;
        var current = periodStart;

        while (current < periodEnd)
        {
            var rate = GetRateForDate(current);
            var dailyRate = CalculateDailyRate(rate);
            var dailyInterest = currentPrincipal * dailyRate;

            breakdown.Add(new DailyAccrualEntry(
                Date: current,
                Principal: currentPrincipal,
                Rate: rate,
                DailyRate: dailyRate,
                Interest: dailyInterest
            ));

            totalInterest += dailyInterest;

            // Compound if applicable
            if (CompoundingMethod == Compounding.Daily)
            {
                currentPrincipal += dailyInterest;
            }
            else if (CompoundingMethod == Compounding.Monthly && IsEndOfMonth(current))
            {
                currentPrincipal += totalInterest;
            }

            current = current.AddDays(1);
        }

        var daysAccrued = (periodEnd.DayNumber - periodStart.DayNumber);
        var roundedInterest = Math.Round(totalInterest, principal.Currency.MinorUnits, MidpointRounding.AwayFromZero);
        var effectiveRate = principal.Amount > 0
            ? (totalInterest / principal.Amount) * (365m / daysAccrued)
            : 0m;

        var formula = BuildFormula(principal.Amount, daysAccrued);

        return new InterestAccrualResult(
            Interest: new Money(roundedInterest, principal.Currency),
            DaysAccrued: daysAccrued,
            EffectiveRate: Math.Round(effectiveRate, 6),
            Formula: formula,
            Breakdown: breakdown.AsReadOnly(),
            RuleKey: RuleKey
        );
    }

    private decimal GetRateForDate(DateOnly date)
    {
        // Find the applicable rate entry (most recent effective before or on date)
        var applicable = RateSchedule
            .Where(r => r.EffectiveFrom <= date && (!r.EffectiveTo.HasValue || r.EffectiveTo.Value >= date))
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefault();

        return applicable?.AnnualRate ?? RateSchedule.First().AnnualRate;
    }

    private decimal CalculateDailyRate(decimal annualRate)
    {
        var daysInYear = DayCountBasis switch
        {
            DayCountBasis.Actual360 => 360m,
            DayCountBasis.Actual365 => 365m,
            DayCountBasis.ThirtyE360 => 360m,
            _ => 365m
        };

        return annualRate / daysInYear;
    }

    private static bool IsEndOfMonth(DateOnly date)
    {
        return date.Day == DateTime.DaysInMonth(date.Year, date.Month);
    }

    private string BuildFormula(decimal principal, int days)
    {
        var rates = string.Join(", ", RateSchedule.Select(r => $"{r.AnnualRate:P2}"));
        return $"Principal({principal:N2}) × Rate({rates}) × Days({days}) / {(DayCountBasis == DayCountBasis.Actual360 ? 360 : 365)} [{CompoundingMethod}]";
    }
}

/// <summary>
/// Entry in a rate schedule (supports variable/stepped rates).
/// </summary>
public sealed record RateScheduleEntry(
    decimal AnnualRate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null,
    string? Label = null
);

/// <summary>
/// When interest begins to accrue.
/// </summary>
public enum AccrualTrigger
{
    /// <summary>Interest accrues from disbursement.</summary>
    FromDisbursement,

    /// <summary>Interest accrues only when overdue.</summary>
    OnOverdue,

    /// <summary>Interest accrues from statement date (credit cards).</summary>
    FromStatementDate
}

/// <summary>
/// Result of interest accrual calculation.
/// </summary>
public sealed record InterestAccrualResult(
    Money Interest,
    int DaysAccrued,
    decimal EffectiveRate,
    string Formula,
    IReadOnlyList<DailyAccrualEntry> Breakdown,
    string RuleKey
);

/// <summary>
/// Single day's accrual entry for audit trail.
/// </summary>
public sealed record DailyAccrualEntry(
    DateOnly Date,
    decimal Principal,
    decimal Rate,
    decimal DailyRate,
    decimal Interest
);