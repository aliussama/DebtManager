using DebtManager.Domain.Services.Finance;
using DebtManager.Domain.Services.Rules;
using DebtManager.Domain.ValueObjects;
using Xunit;

namespace DebtManager.Domain.Tests.Rules;

public class InterestAccrualRuleTests
{
    [Fact]
    public void Calculate_SimpleInterest_ProducesCorrectAmount()
    {
        // Arrange: 10,000 EGP at 12% annual for 30 days
        var rule = new InterestAccrualRule(
            ruleKey: "simple_12pct",
            rateSchedule: new[] { new RateScheduleEntry(0.12m, new DateOnly(2025, 1, 1)) },
            compoundingMethod: Compounding.Simple,
            dayCountBasis: DayCountBasis.Actual365
        );

        var principal = new Money(10_000m, Currency.EGP);
        var start = new DateOnly(2025, 6, 1);
        var end = new DateOnly(2025, 7, 1); // 30 days

        // Act
        var result = rule.Calculate(principal, start, end);

        // Assert: 10000 × 0.12 × (30/365) ≈ 98.63
        Assert.Equal(30, result.DaysAccrued);
        Assert.InRange(result.Interest.Amount, 98m, 99m);
        Assert.Contains("10,000", result.Formula);
    }

    [Fact]
    public void Calculate_DailyCompounding_ProducesHigherAmount()
    {
        // Arrange
        var simpleRule = new InterestAccrualRule(
            ruleKey: "simple",
            rateSchedule: new[] { new RateScheduleEntry(0.12m, new DateOnly(2025, 1, 1)) },
            compoundingMethod: Compounding.Simple
        );

        var compoundRule = new InterestAccrualRule(
            ruleKey: "compound",
            rateSchedule: new[] { new RateScheduleEntry(0.12m, new DateOnly(2025, 1, 1)) },
            compoundingMethod: Compounding.Daily
        );

        var principal = new Money(10_000m, Currency.EGP);
        var start = new DateOnly(2025, 1, 1);
        var end = new DateOnly(2025, 12, 31); // ~365 days

        // Act
        var simpleResult = simpleRule.Calculate(principal, start, end);
        var compoundResult = compoundRule.Calculate(principal, start, end);

        // Assert: Compound should be higher
        Assert.True(compoundResult.Interest.Amount > simpleResult.Interest.Amount);
    }

    [Fact]
    public void Calculate_WithGracePeriod_SkipsGraceDays()
    {
        // Arrange: 5 day grace before interest accrues
        var rule = new InterestAccrualRule(
            ruleKey: "with_grace",
            rateSchedule: new[] { new RateScheduleEntry(0.12m, new DateOnly(2025, 1, 1)) },
            graceDaysBeforeAccrual: 5
        );

        var principal = new Money(10_000m, Currency.EGP);
        var dueDate = new DateOnly(2025, 6, 1);
        var start = new DateOnly(2025, 6, 1);
        var end = new DateOnly(2025, 6, 4); // Within grace

        // Act
        var result = rule.Calculate(principal, start, end, dueDate);

        // Assert: No interest within grace
        Assert.Equal(0, result.DaysAccrued);
        Assert.Equal(0m, result.Interest.Amount);
        Assert.Contains("grace", result.Formula.ToLowerInvariant());
    }

    [Fact]
    public void Calculate_VariableRates_AppliesCorrectRatePerPeriod()
    {
        // Arrange: Intro rate 6% for first 3 months, then 12%
        var rule = new InterestAccrualRule(
            ruleKey: "variable",
            rateSchedule: new[]
            {
                new RateScheduleEntry(0.06m, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31), "Intro"),
                new RateScheduleEntry(0.12m, new DateOnly(2025, 4, 1), null, "Standard")
            }
        );

        var principal = new Money(10_000m, Currency.EGP);

        // Act: Calculate for intro period
        var introResult = rule.Calculate(principal, new DateOnly(2025, 2, 1), new DateOnly(2025, 3, 1));

        // Act: Calculate for standard period
        var standardResult = rule.Calculate(principal, new DateOnly(2025, 5, 1), new DateOnly(2025, 6, 1));

        // Assert: Standard rate produces ~2x the interest
        Assert.True(standardResult.Interest.Amount > introResult.Interest.Amount * 1.8m);
    }

    [Fact]
    public void Calculate_ProducesDetailedBreakdown()
    {
        // Arrange
        var rule = new InterestAccrualRule(
            ruleKey: "detailed",
            rateSchedule: new[] { new RateScheduleEntry(0.10m, new DateOnly(2025, 1, 1)) }
        );

        var principal = new Money(5_000m, Currency.EGP);
        var start = new DateOnly(2025, 6, 1);
        var end = new DateOnly(2025, 6, 11); // 10 days

        // Act
        var result = rule.Calculate(principal, start, end);

        // Assert
        Assert.Equal(10, result.Breakdown.Count);
        Assert.All(result.Breakdown, entry =>
        {
            Assert.True(entry.Principal > 0);
            Assert.True(entry.DailyRate > 0);
            Assert.True(entry.Interest > 0);
        });
    }

    [Fact]
    public void Calculate_Actual360Basis_UsesCorrectDivisor()
    {
        // Arrange
        var rule365 = new InterestAccrualRule(
            ruleKey: "365",
            rateSchedule: new[] { new RateScheduleEntry(0.12m, new DateOnly(2025, 1, 1)) },
            dayCountBasis: DayCountBasis.Actual365
        );

        var rule360 = new InterestAccrualRule(
            ruleKey: "360",
            rateSchedule: new[] { new RateScheduleEntry(0.12m, new DateOnly(2025, 1, 1)) },
            dayCountBasis: DayCountBasis.Actual360
        );

        var principal = new Money(10_000m, Currency.EGP);
        var start = new DateOnly(2025, 6, 1);
        var end = new DateOnly(2025, 7, 1);

        // Act
        var result365 = rule365.Calculate(principal, start, end);
        var result360 = rule360.Calculate(principal, start, end);

        // Assert: 360 basis produces slightly higher interest
        Assert.True(result360.Interest.Amount > result365.Interest.Amount);
    }
}