using DebtManager.Domain.Scheduling;
using DebtManager.Domain.ValueObjects;
using Xunit;

namespace DebtManager.Domain.Tests.Scheduling;

public class RecurringScheduleSpecTests
{
    [Fact]
    public void Expand_Monthly_Generates12InstallmentsPerYear()
    {
        // Arrange
        var spec = new RecurringScheduleSpec(
            scheduleId: Guid.NewGuid(),
            obligationId: Guid.NewGuid(),
            pattern: RecurrencePattern.Monthly,
            dayOfMonth: 15,
            startDate: new DateOnly(2025, 1, 1),
            endDate: new DateOnly(2025, 12, 31),
            maxOccurrences: null,
            installmentAmount: new Money(5000m, Currency.EGP)
        );

        // Act
        var installments = spec.Expand(
            from: new DateOnly(2025, 1, 1),
            to: new DateOnly(2025, 12, 31)
        ).ToList();

        // Assert
        Assert.Equal(12, installments.Count);
        Assert.All(installments, i => Assert.Equal(15, i.DueDate.Day));
    }

    [Fact]
    public void Expand_Quarterly_Generates4InstallmentsPerYear()
    {
        // Arrange
        var spec = new RecurringScheduleSpec(
            scheduleId: Guid.NewGuid(),
            obligationId: Guid.NewGuid(),
            pattern: RecurrencePattern.Quarterly,
            dayOfMonth: 1,
            startDate: new DateOnly(2025, 1, 1),
            endDate: null,
            maxOccurrences: 4,
            installmentAmount: new Money(25000m, Currency.EGP)
        );

        // Act
        var installments = spec.Expand(
            from: new DateOnly(2025, 1, 1),
            to: new DateOnly(2025, 12, 31)
        ).ToList();

        // Assert
        Assert.Equal(4, installments.Count);
    }

    [Fact]
    public void Expand_WithMaxOccurrences_StopsAtLimit()
    {
        // Arrange
        var spec = new RecurringScheduleSpec(
            scheduleId: Guid.NewGuid(),
            obligationId: Guid.NewGuid(),
            pattern: RecurrencePattern.Monthly,
            dayOfMonth: 10,
            startDate: new DateOnly(2025, 1, 1),
            endDate: null,
            maxOccurrences: 6,
            installmentAmount: new Money(1000m, Currency.EGP)
        );

        // Act: Request 2 years, but max is 6
        var installments = spec.Expand(
            from: new DateOnly(2025, 1, 1),
            to: new DateOnly(2026, 12, 31)
        ).ToList();

        // Assert
        Assert.Equal(6, installments.Count);
    }

    [Fact]
    public void Expand_WithWeekendAdjustment_ShiftsDates()
    {
        // Arrange: 15th March 2025 is Saturday
        var spec = new RecurringScheduleSpec(
            scheduleId: Guid.NewGuid(),
            obligationId: Guid.NewGuid(),
            pattern: RecurrencePattern.Monthly,
            dayOfMonth: 15,
            startDate: new DateOnly(2025, 3, 1),
            endDate: new DateOnly(2025, 3, 31),
            maxOccurrences: 1,
            installmentAmount: new Money(1000m, Currency.EGP),
            weekendAdjustment: WeekendAdjustment.NextBusinessDay
        );

        // Act
        var installments = spec.Expand(
            from: new DateOnly(2025, 3, 1),
            to: new DateOnly(2025, 3, 31)
        ).ToList();

        // Assert: Should be Monday the 17th
        Assert.Single(installments);
        Assert.Equal(new DateOnly(2025, 3, 17), installments[0].DueDate);
    }

    [Fact]
    public void Expand_ClampsToLastDayOfMonth()
    {
        // Arrange: Day 31 in February
        var spec = new RecurringScheduleSpec(
            scheduleId: Guid.NewGuid(),
            obligationId: Guid.NewGuid(),
            pattern: RecurrencePattern.Monthly,
            dayOfMonth: 31,
            startDate: new DateOnly(2025, 1, 1),
            endDate: new DateOnly(2025, 3, 31),
            maxOccurrences: null,
            installmentAmount: new Money(2000m, Currency.EGP)
        );

        // Act
        var installments = spec.Expand(
            from: new DateOnly(2025, 1, 1),
            to: new DateOnly(2025, 3, 31)
        ).ToList();

        // Assert
        Assert.Equal(3, installments.Count);
        Assert.Equal(31, installments[0].DueDate.Day); // Jan 31
        Assert.Equal(28, installments[1].DueDate.Day); // Feb 28 (2025 is not leap year)
        Assert.Equal(31, installments[2].DueDate.Day); // Mar 31
    }

    [Fact]
    public void Expand_Annual_GeneratesOnePerYear()
    {
        // Arrange
        var spec = new RecurringScheduleSpec(
            scheduleId: Guid.NewGuid(),
            obligationId: Guid.NewGuid(),
            pattern: RecurrencePattern.Annual,
            dayOfMonth: 1,
            startDate: new DateOnly(2025, 6, 1),
            endDate: null,
            maxOccurrences: 5,
            installmentAmount: new Money(50000m, Currency.EGP)
        );

        // Act
        var installments = spec.Expand(
            from: new DateOnly(2025, 1, 1),
            to: new DateOnly(2030, 12, 31)
        ).ToList();

        // Assert
        Assert.Equal(5, installments.Count);
        Assert.Equal(new DateOnly(2025, 6, 1), installments[0].DueDate);
        Assert.Equal(new DateOnly(2029, 6, 1), installments[4].DueDate);
    }
}