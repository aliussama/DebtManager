using DebtManager.Domain.Services.Rules;
using Xunit;

namespace DebtManager.Domain.Tests.Rules;

public class GracePeriodRuleTests
{
    [Fact]
    public void Evaluate_WithinGracePeriod_ReturnsTrue()
    {
        // Arrange
        var rule = new GracePeriodRule(
            ruleKey: "grace_7_days",
            graceDays: 7
        );
        var dueDate = new DateOnly(2025, 6, 1);
        var evaluationDate = new DateOnly(2025, 6, 5); // 4 days after

        // Act
        var result = rule.Evaluate(dueDate, evaluationDate);

        // Assert
        Assert.True(result.IsWithinGrace);
        Assert.Equal(4, result.DaysOverdue);
        Assert.Equal(4, result.DaysIntoGrace);
    }

    [Fact]
    public void Evaluate_AfterGracePeriod_ReturnsFalse()
    {
        // Arrange
        var rule = new GracePeriodRule(
            ruleKey: "grace_7_days",
            graceDays: 7
        );
        var dueDate = new DateOnly(2025, 6, 1);
        var evaluationDate = new DateOnly(2025, 6, 15); // 14 days after

        // Act
        var result = rule.Evaluate(dueDate, evaluationDate);

        // Assert
        Assert.False(result.IsWithinGrace);
        Assert.Equal(14, result.DaysOverdue);
        Assert.Equal(new DateOnly(2025, 6, 8), result.GraceExpiryDate);
    }

    [Fact]
    public void Evaluate_OnDueDate_IsWithinGrace()
    {
        // Arrange
        var rule = new GracePeriodRule(
            ruleKey: "grace_5_days",
            graceDays: 5
        );
        var dueDate = new DateOnly(2025, 6, 1);

        // Act
        var result = rule.Evaluate(dueDate, dueDate);

        // Assert
        Assert.True(result.IsWithinGrace);
        Assert.Equal(0, result.DaysOverdue);
    }

    [Fact]
    public void CalculateEffectiveDaysOverdue_WithinGrace_ReturnsZero()
    {
        // Arrange
        var rule = new GracePeriodRule(
            ruleKey: "grace_10_days",
            graceDays: 10
        );
        var dueDate = new DateOnly(2025, 6, 1);
        var evaluationDate = new DateOnly(2025, 6, 8); // 7 days, within grace

        // Act
        var effectiveDays = rule.CalculateEffectiveDaysOverdue(dueDate, evaluationDate);

        // Assert
        Assert.Equal(0, effectiveDays);
    }

    [Fact]
    public void CalculateEffectiveDaysOverdue_AfterGrace_ReturnsAdjusted()
    {
        // Arrange
        var rule = new GracePeriodRule(
            ruleKey: "grace_10_days",
            graceDays: 10
        );
        var dueDate = new DateOnly(2025, 6, 1);
        var evaluationDate = new DateOnly(2025, 6, 20); // 19 days

        // Act
        var effectiveDays = rule.CalculateEffectiveDaysOverdue(dueDate, evaluationDate);

        // Assert: 19 - 10 = 9 effective days overdue
        Assert.Equal(9, effectiveDays);
    }

    [Fact]
    public void Evaluate_BusinessDaysGrace_SkipsWeekends()
    {
        // Arrange: Friday June 6, 2025 + 3 business days = Wednesday June 11
        var rule = new GracePeriodRule(
            ruleKey: "grace_3_business",
            graceDays: 3,
            type: GracePeriodType.BusinessDays
        );
        var dueDate = new DateOnly(2025, 6, 6); // Friday

        // Act
        var result = rule.Evaluate(dueDate, new DateOnly(2025, 6, 10)); // Tuesday

        // Assert: Still within 3 business days
        Assert.True(result.IsWithinGrace);
        Assert.Equal(new DateOnly(2025, 6, 11), result.GraceExpiryDate); // Wednesday
    }
}