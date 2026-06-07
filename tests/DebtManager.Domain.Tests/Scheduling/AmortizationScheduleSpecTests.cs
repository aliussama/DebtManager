using DebtManager.Domain.Scheduling;
using DebtManager.Domain.ValueObjects;
using Xunit;

namespace DebtManager.Domain.Tests.Scheduling;

public class AmortizationScheduleSpecTests
{
    [Fact]
    public void CalculateMonthlyPayment_StandardLoan_ReturnsCorrectPMT()
    {
        // Arrange: $100,000 loan, 6% annual rate, 30 years (360 months)
        var spec = new AmortizationScheduleSpec(
            scheduleId: Guid.NewGuid(),
            obligationId: Guid.NewGuid(),
            principal: new Money(100_000m, Currency.USD),
            annualInterestRate: 0.06m,
            termInMonths: 360,
            firstPaymentDate: new DateOnly(2025, 2, 1),
            dayOfMonth: 1
        );

        // Act
        var monthlyPayment = spec.CalculateMonthlyPayment();

        // Assert: PMT should be approximately $599.55
        Assert.InRange(monthlyPayment, 599.50m, 599.60m);
    }

    [Fact]
    public void CalculateMonthlyPayment_ZeroInterest_ReturnsSimpleDivision()
    {
        // Arrange: 12,000 EGP loan, 0% rate, 12 months
        var spec = new AmortizationScheduleSpec(
            scheduleId: Guid.NewGuid(),
            obligationId: Guid.NewGuid(),
            principal: new Money(12_000m, Currency.EGP),
            annualInterestRate: 0m,
            termInMonths: 12,
            firstPaymentDate: new DateOnly(2025, 1, 15),
            dayOfMonth: 15
        );

        // Act
        var monthlyPayment = spec.CalculateMonthlyPayment();

        // Assert
        Assert.Equal(1000m, monthlyPayment);
    }

    [Fact]
    public void GenerateAmortizationSchedule_ProducesCorrectNumberOfEntries()
    {
        // Arrange
        var spec = new AmortizationScheduleSpec(
            scheduleId: Guid.NewGuid(),
            obligationId: Guid.NewGuid(),
            principal: new Money(50_000m, Currency.EGP),
            annualInterestRate: 0.12m,
            termInMonths: 24,
            firstPaymentDate: new DateOnly(2025, 1, 1),
            dayOfMonth: 1
        );

        // Act
        var schedule = spec.GenerateAmortizationSchedule();

        // Assert
        Assert.Equal(24, schedule.Count);
        Assert.Equal(1, schedule.First().PaymentNumber);
        Assert.Equal(24, schedule.Last().PaymentNumber);
    }

    [Fact]
    public void GenerateAmortizationSchedule_FinalBalanceIsZero()
    {
        // Arrange
        var spec = new AmortizationScheduleSpec(
            scheduleId: Guid.NewGuid(),
            obligationId: Guid.NewGuid(),
            principal: new Money(100_000m, Currency.EGP),
            annualInterestRate: 0.10m,
            termInMonths: 60,
            firstPaymentDate: new DateOnly(2025, 3, 15),
            dayOfMonth: 15
        );

        // Act
        var schedule = spec.GenerateAmortizationSchedule();

        // Assert
        Assert.Equal(0m, schedule.Last().RemainingBalance.Amount);
    }

    [Fact]
    public void GenerateAmortizationSchedule_PrincipalPlusInterestEqualsTotalPayment()
    {
        // Arrange
        var spec = new AmortizationScheduleSpec(
            scheduleId: Guid.NewGuid(),
            obligationId: Guid.NewGuid(),
            principal: new Money(25_000m, Currency.USD),
            annualInterestRate: 0.08m,
            termInMonths: 36,
            firstPaymentDate: new DateOnly(2025, 1, 1),
            dayOfMonth: 1
        );

        // Act
        var schedule = spec.GenerateAmortizationSchedule();

        // Assert: For each entry, principal + interest = total (within rounding)
        foreach (var entry in schedule)
        {
            var sum = entry.PrincipalPortion.Amount + entry.InterestPortion.Amount;
            Assert.InRange(sum, entry.TotalPayment.Amount - 0.02m, entry.TotalPayment.Amount + 0.02m);
        }
    }

    [Fact]
    public void CalculateTotalInterest_ReturnsPositiveAmount()
    {
        // Arrange
        var spec = new AmortizationScheduleSpec(
            scheduleId: Guid.NewGuid(),
            obligationId: Guid.NewGuid(),
            principal: new Money(200_000m, Currency.EGP),
            annualInterestRate: 0.15m,
            termInMonths: 120,
            firstPaymentDate: new DateOnly(2025, 1, 1),
            dayOfMonth: 1
        );

        // Act
        var totalInterest = spec.CalculateTotalInterest();

        // Assert
        Assert.True(totalInterest.Amount > 0);
        Assert.True(totalInterest.Amount > spec.Principal.Amount * 0.5m); // Significant interest over 10 years at 15%
    }

    [Fact]
    public void Expand_ReturnsInstallmentsWithinDateRange()
    {
        // Arrange
        var spec = new AmortizationScheduleSpec(
            scheduleId: Guid.NewGuid(),
            obligationId: Guid.NewGuid(),
            principal: new Money(60_000m, Currency.EGP),
            annualInterestRate: 0.10m,
            termInMonths: 60,
            firstPaymentDate: new DateOnly(2025, 1, 1),
            dayOfMonth: 1
        );

        // Act: Request only 2025
        var installments = spec.Expand(
            from: new DateOnly(2025, 1, 1),
            to: new DateOnly(2025, 12, 31)
        ).ToList();

        // Assert
        Assert.Equal(12, installments.Count);
        Assert.All(installments, i => Assert.InRange(i.DueDate, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)));
    }
}