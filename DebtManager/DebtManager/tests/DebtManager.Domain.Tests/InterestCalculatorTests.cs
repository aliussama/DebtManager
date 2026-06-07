using DebtManager.Domain.Services.Finance;
using DebtManager.Domain.ValueObjects;
using Xunit;

namespace DebtManager.Domain.Tests;

public class InterestCalculatorTests
{
    [Fact]
    public void SimpleInterest_Actual365_Works()
    {
        var p = new Money(10000m, Currency.EGP);
        var i = InterestCalculator.Accrue(
            p, annualRate: 0.12m,
            from: new DateOnly(2026, 1, 1),
            toExclusive: new DateOnly(2026, 2, 1),
            basis: DayCountBasis.Actual365,
            compounding: Compounding.Simple);

        Assert.True(i.Amount > 0m);
    }

    [Fact]
    public void DailyCompounding_GreaterThanSimple_ForSamePeriod()
    {
        var p = new Money(10000m, Currency.EGP);
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 4, 1);

        var simple = InterestCalculator.Accrue(p, 0.18m, from, to, DayCountBasis.Actual365, Compounding.Simple);
        var daily = InterestCalculator.Accrue(p, 0.18m, from, to, DayCountBasis.Actual365, Compounding.Daily);

        Assert.True(daily.Amount >= simple.Amount);
    }

    [Fact]
    public void ZeroDays_ReturnsZero()
    {
        var p = new Money(10000m, Currency.EGP);
        var i = InterestCalculator.Accrue(p, 0.2m, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), DayCountBasis.Actual365, Compounding.Daily);
        Assert.Equal(0m, i.Amount);
    }
}
