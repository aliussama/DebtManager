using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Services.Finance;

public enum DayCountBasis
{
    Actual365,
    Actual360,
    ThirtyE360
}

public enum Compounding
{
    Simple,
    Daily,
    Monthly
}

public static class InterestCalculator
{
    public static Money Accrue(
        Money principal,
        decimal annualRate,
        DateOnly from,
        DateOnly toExclusive,
        DayCountBasis basis,
        Compounding compounding)
    {
        if (toExclusive <= from) return Money.Zero(principal.Currency);

        var days = DaysBetween(from, toExclusive);
        if (days <= 0) return Money.Zero(principal.Currency);

        var p = principal.Amount;
        var r = annualRate;

        decimal interest = compounding switch
        {
            Compounding.Simple => SimpleInterest(p, r, days, basis),
            Compounding.Daily => DailyCompounding(p, r, days, basis),
            Compounding.Monthly => MonthlyCompounding(p, r, from, toExclusive, basis),
            _ => 0m
        };

        // interest is always >= 0 unless rate negative (support later)
        if (interest < 0m) interest = 0m;

        return new Money(Decimal.Round(interest, 2), principal.Currency);
    }

    private static int DaysBetween(DateOnly from, DateOnly toExclusive)
        => toExclusive.DayNumber - from.DayNumber;

    private static decimal Denominator(DayCountBasis basis, DateOnly from, DateOnly toExclusive, int days)
        => basis switch
        {
            DayCountBasis.Actual365 => 365m,
            DayCountBasis.Actual360 => 360m,
            DayCountBasis.ThirtyE360 => 360m,
            _ => 365m
        };

    private static decimal SimpleInterest(decimal principal, decimal annualRate, int days, DayCountBasis basis)
    {
        var denom = Denominator(basis, default, default, days);
        return principal * annualRate * (days / denom);
    }

    private static decimal DailyCompounding(decimal principal, decimal annualRate, int days, DayCountBasis basis)
    {
        var denom = Denominator(basis, default, default, days);
        var daily = annualRate / denom;
        // (1 + daily)^days - 1
        var factor = (decimal)Math.Pow((double)(1m + daily), days);
        return principal * (factor - 1m);
    }

    private static decimal MonthlyCompounding(decimal principal, decimal annualRate, DateOnly from, DateOnly toExclusive, DayCountBasis basis)
    {
        // For v1: approximate monthly compounding by counting whole months + remaining days as simple.
        // This is still deterministic and upgradeable later.
        var months = CountWholeMonths(from, toExclusive);
        var afterMonths = from.AddMonths(months);
        var remainingDays = DaysBetween(afterMonths, toExclusive);

        var monthlyRate = annualRate / 12m;
        var factor = (decimal)Math.Pow((double)(1m + monthlyRate), months);
        var pAfter = principal * factor;

        var denom = Denominator(basis, afterMonths, toExclusive, remainingDays);
        var remInterest = pAfter * annualRate * (remainingDays / denom);

        return (pAfter - principal) + remInterest;
    }

    private static int CountWholeMonths(DateOnly from, DateOnly toExclusive)
    {
        var months = (toExclusive.Year - from.Year) * 12 + (toExclusive.Month - from.Month);
        var candidate = from.AddMonths(months);
        if (candidate > toExclusive) months--;
        return Math.Max(0, months);
    }
}
