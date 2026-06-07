using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Scheduling;

/// <summary>
/// Schedule specification for amortizing loans using the PMT formula.
/// Generates installments with principal + interest breakdown.
/// </summary>
public sealed class AmortizationScheduleSpec : IScheduleSpec
{
    public Guid ScheduleId { get; }
    public Guid ObligationId { get; }
    public Money Principal { get; }
    public decimal AnnualInterestRate { get; }
    public int TermInMonths { get; }
    public DateOnly FirstPaymentDate { get; }
    public int DayOfMonth { get; }
    public AmortizationType AmortizationType { get; }
    public WeekendAdjustment WeekendAdjustment { get; }

    public AmortizationScheduleSpec(
        Guid scheduleId,
        Guid obligationId,
        Money principal,
        decimal annualInterestRate,
        int termInMonths,
        DateOnly firstPaymentDate,
        int dayOfMonth,
        AmortizationType amortizationType = AmortizationType.FullyAmortizing,
        WeekendAdjustment weekendAdjustment = WeekendAdjustment.None)
    {
        if (scheduleId == Guid.Empty)
            throw new ArgumentException("Schedule ID cannot be empty.", nameof(scheduleId));
        if (obligationId == Guid.Empty)
            throw new ArgumentException("Obligation ID cannot be empty.", nameof(obligationId));
        if (principal.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(principal), "Principal must be positive.");
        if (annualInterestRate < 0)
            throw new ArgumentOutOfRangeException(nameof(annualInterestRate), "Interest rate cannot be negative.");
        if (termInMonths < 1)
            throw new ArgumentOutOfRangeException(nameof(termInMonths), "Term must be at least 1 month.");
        if (dayOfMonth < 1 || dayOfMonth > 31)
            throw new ArgumentOutOfRangeException(nameof(dayOfMonth), "Day of month must be between 1 and 31.");

        ScheduleId = scheduleId;
        ObligationId = obligationId;
        Principal = principal;
        AnnualInterestRate = annualInterestRate;
        TermInMonths = termInMonths;
        FirstPaymentDate = firstPaymentDate;
        DayOfMonth = dayOfMonth;
        AmortizationType = amortizationType;
        WeekendAdjustment = weekendAdjustment;
    }

    /// <summary>
    /// Expand the amortization schedule into concrete installments.
    /// </summary>
    public IEnumerable<Installment> Expand(DateOnly from, DateOnly to)
    {
        var schedule = GenerateAmortizationSchedule();

        foreach (var entry in schedule)
        {
            if (entry.DueDate < from || entry.DueDate > to)
                continue;

            var adjustedDate = AdjustForWeekend(entry.DueDate);
            var installmentKey = GenerateInstallmentKey(entry.PaymentNumber);

            yield return new Installment(
                InstallmentKey: installmentKey,
                ObligationId: ObligationId,
                DueDate: adjustedDate,
                ExpectedAmount: entry.TotalPayment,
                ScheduleOrigin: $"amortization:{ScheduleId}",
                Tags: new[] { "amortization", $"principal:{entry.PrincipalPortion.Amount:F2}", $"interest:{entry.InterestPortion.Amount:F2}" }
            );
        }
    }

    /// <summary>
    /// Generate the full amortization schedule with principal/interest breakdown.
    /// </summary>
    public IReadOnlyList<AmortizationEntry> GenerateAmortizationSchedule()
    {
        var entries = new List<AmortizationEntry>();
        var monthlyPayment = CalculateMonthlyPayment();
        var balance = Principal.Amount;
        var monthlyRate = AnnualInterestRate / 12m;
        var currency = Principal.Currency;

        var currentDate = FirstPaymentDate;

        for (var i = 1; i <= TermInMonths && balance > 0; i++)
        {
            var interestPortion = Math.Round(balance * monthlyRate, currency.MinorUnits, MidpointRounding.AwayFromZero);
            var principalPortion = Math.Min(monthlyPayment - interestPortion, balance);

            // Handle final payment rounding
            if (i == TermInMonths || principalPortion >= balance)
            {
                principalPortion = balance;
                monthlyPayment = principalPortion + interestPortion;
            }

            var newBalance = balance - principalPortion;

            entries.Add(new AmortizationEntry(
                PaymentNumber: i,
                DueDate: GetPaymentDate(currentDate, i - 1),
                TotalPayment: new Money(Math.Round(monthlyPayment, currency.MinorUnits, MidpointRounding.AwayFromZero), currency),
                PrincipalPortion: new Money(principalPortion, currency),
                InterestPortion: new Money(interestPortion, currency),
                RemainingBalance: new Money(Math.Max(0, newBalance), currency)
            ));

            balance = newBalance;
        }

        return entries;
    }

    /// <summary>
    /// Calculate the monthly payment using the PMT formula.
    /// PMT = P × [r(1+r)^n] / [(1+r)^n - 1]
    /// </summary>
    public decimal CalculateMonthlyPayment()
    {
        var P = Principal.Amount;
        var r = AnnualInterestRate / 12m;
        var n = TermInMonths;

        if (r == 0m)
        {
            // Zero interest: simple division
            return Math.Round(P / n, Principal.Currency.MinorUnits, MidpointRounding.AwayFromZero);
        }

        // PMT formula using decimal math for precision
        var onePlusR = 1m + r;
        var onePlusRtoN = DecimalPow(onePlusR, n);

        var numerator = r * onePlusRtoN;
        var denominator = onePlusRtoN - 1m;

        var pmt = P * (numerator / denominator);

        return Math.Round(pmt, Principal.Currency.MinorUnits, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Calculate total interest over the life of the loan.
    /// </summary>
    public Money CalculateTotalInterest()
    {
        var monthlyPayment = CalculateMonthlyPayment();
        var totalPayments = monthlyPayment * TermInMonths;
        var totalInterest = totalPayments - Principal.Amount;

        return new Money(
            Math.Round(Math.Max(0, totalInterest), Principal.Currency.MinorUnits, MidpointRounding.AwayFromZero),
            Principal.Currency
        );
    }

    /// <summary>
    /// Calculate the effective annual rate (APR) accounting for compounding.
    /// </summary>
    public decimal CalculateEffectiveAnnualRate()
    {
        if (AnnualInterestRate == 0m) return 0m;

        var monthlyRate = AnnualInterestRate / 12m;
        var effectiveRate = DecimalPow(1m + monthlyRate, 12) - 1m;

        return Math.Round(effectiveRate, 6);
    }

    private DateOnly GetPaymentDate(DateOnly firstPayment, int monthsToAdd)
    {
        var date = firstPayment.AddMonths(monthsToAdd);
        var clampedDay = Math.Min(DayOfMonth, DateTime.DaysInMonth(date.Year, date.Month));
        return new DateOnly(date.Year, date.Month, clampedDay);
    }

    private DateOnly AdjustForWeekend(DateOnly date)
    {
        var dayOfWeek = date.DayOfWeek;

        return WeekendAdjustment switch
        {
            WeekendAdjustment.NextBusinessDay => dayOfWeek switch
            {
                DayOfWeek.Saturday => date.AddDays(2),
                DayOfWeek.Sunday => date.AddDays(1),
                _ => date
            },
            WeekendAdjustment.PreviousBusinessDay => dayOfWeek switch
            {
                DayOfWeek.Saturday => date.AddDays(-1),
                DayOfWeek.Sunday => date.AddDays(-2),
                _ => date
            },
            _ => date
        };
    }

    private string GenerateInstallmentKey(int paymentNumber)
    {
        return $"{ScheduleId:N}:amort:{paymentNumber:D4}";
    }

    /// <summary>
    /// Decimal power function for financial calculations.
    /// More precise than Math.Pow for monetary values.
    /// </summary>
    private static decimal DecimalPow(decimal baseValue, int exponent)
    {
        if (exponent == 0) return 1m;
        if (exponent < 0) return 1m / DecimalPow(baseValue, -exponent);

        var result = 1m;
        for (var i = 0; i < exponent; i++)
        {
            result *= baseValue;
        }
        return result;
    }
}

/// <summary>
/// A single entry in an amortization schedule.
/// </summary>
public sealed record AmortizationEntry(
    int PaymentNumber,
    DateOnly DueDate,
    Money TotalPayment,
    Money PrincipalPortion,
    Money InterestPortion,
    Money RemainingBalance
);

/// <summary>
/// Type of amortization calculation.
/// </summary>
public enum AmortizationType
{
    /// <summary>Standard fully amortizing loan (principal + interest each payment).</summary>
    FullyAmortizing,

    /// <summary>Interest-only payments with balloon principal at end.</summary>
    InterestOnly,

    /// <summary>Equal principal payments with decreasing interest.</summary>
    EqualPrincipal
}