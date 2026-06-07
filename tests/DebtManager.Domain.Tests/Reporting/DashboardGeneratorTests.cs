using DebtManager.Domain.Projections.Charges;
using DebtManager.Domain.Projections.Installments;
using DebtManager.Domain.ValueObjects;
using DebtManager.Reporting.Models;
using DebtManager.Reporting.Services;
using Xunit;

namespace DebtManager.Domain.Tests.Reporting;

public class DashboardGeneratorTests
{
    private readonly DashboardGenerator _generator = new();

    [Fact]
    public void Generate_WithNoObligations_ReturnsEmptyDashboard()
    {
        // Arrange
        var obligations = Array.Empty<ObligationSnapshot>();
        var asOfDate = new DateOnly(2025, 6, 15);

        // Act
        var dashboard = _generator.Generate(obligations, asOfDate, Currency.EGP);

        // Assert
        Assert.Equal(asOfDate, dashboard.AsOfDate);
        Assert.Equal("EGP", dashboard.CurrencyCode);
        Assert.Equal(0, dashboard.TotalObligations);
        Assert.Equal(0m, dashboard.TotalPrincipal.Amount);
    }

    [Fact]
    public void Generate_WithSingleObligation_CalculatesTotals()
    {
        // Arrange
        var obligations = new[]
        {
            CreateObligationSnapshot(
                obligationId: Guid.NewGuid(),
                name: "Car Loan",
                principal: 100_000m,
                totalPaid: 25_000m,
                outstanding: 75_000m
            )
        };
        var asOfDate = new DateOnly(2025, 6, 15);

        // Act
        var dashboard = _generator.Generate(obligations, asOfDate, Currency.EGP);

        // Assert
        Assert.Equal(1, dashboard.TotalObligations);
        Assert.Equal(1, dashboard.ActiveObligations);
        Assert.Equal(0, dashboard.ClosedObligations);
        Assert.Equal(100_000m, dashboard.TotalPrincipal.Amount);
        Assert.Equal(75_000m, dashboard.TotalOutstanding.Amount);
    }

    [Fact]
    public void Generate_WithMixedHealthStatus_CountsCorrectly()
    {
        // Arrange
        var asOfDate = new DateOnly(2025, 6, 15);
        var obligations = new[]
        {
            CreateObligationSnapshotWithInstallments(
                name: "Healthy Loan",
                installments: new[]
                {
                    CreateInstallment(asOfDate.AddDays(10), InstallmentStatus.Upcoming)
                }
            ),
            CreateObligationSnapshotWithInstallments(
                name: "At Risk Loan",
                installments: new[]
                {
                    CreateInstallment(asOfDate.AddDays(-15), InstallmentStatus.Overdue) // 15 days overdue
                }
            ),
            CreateObligationSnapshotWithInstallments(
                name: "Delinquent Loan",
                installments: new[]
                {
                    CreateInstallment(asOfDate.AddDays(-60), InstallmentStatus.Overdue) // 60 days overdue
                }
            ),
            CreateObligationSnapshotWithInstallments(
                name: "Critical Loan",
                installments: new[]
                {
                    CreateInstallment(asOfDate.AddDays(-120), InstallmentStatus.Overdue) // 120 days overdue
                }
            )
        };

        // Act
        var dashboard = _generator.Generate(obligations, asOfDate, Currency.EGP);

        // Assert
        Assert.Equal(4, dashboard.ActiveObligations);
        Assert.Equal(1, dashboard.HealthyObligations);
        Assert.Equal(1, dashboard.AtRiskObligations);
        Assert.Equal(1, dashboard.DelinquentObligations);
        Assert.Equal(1, dashboard.CriticalObligations);
    }

    [Fact]
    public void Generate_CountsUpcomingPayments()
    {
        // Arrange
        var asOfDate = new DateOnly(2025, 6, 15);
        var obligations = new[]
        {
            CreateObligationSnapshotWithInstallments(
                name: "Test Loan",
                installments: new[]
                {
                    CreateInstallment(asOfDate.AddDays(3), InstallmentStatus.Upcoming, 1000m), // In 7 days
                    CreateInstallment(asOfDate.AddDays(10), InstallmentStatus.Upcoming, 1000m), // In 30 days
                    CreateInstallment(asOfDate.AddDays(20), InstallmentStatus.Upcoming, 1000m), // In 30 days
                    CreateInstallment(asOfDate.AddDays(45), InstallmentStatus.Upcoming, 1000m)  // Beyond 30 days
                }
            )
        };

        // Act
        var dashboard = _generator.Generate(obligations, asOfDate, Currency.EGP);

        // Assert
        Assert.Equal(1, dashboard.UpcomingPaymentsNext7Days);
        Assert.Equal(3, dashboard.UpcomingPaymentsNext30Days);
        Assert.Equal(1000m, dashboard.TotalDueNext7Days.Amount);
        Assert.Equal(3000m, dashboard.TotalDueNext30Days.Amount);
    }

    [Fact]
    public void Generate_CountsOverdueInstallments()
    {
        // Arrange
        var asOfDate = new DateOnly(2025, 6, 15);
        var obligations = new[]
        {
            CreateObligationSnapshotWithInstallments(
                name: "Test Loan",
                installments: new[]
                {
                    CreateInstallment(asOfDate.AddDays(-10), InstallmentStatus.Overdue, 5000m),
                    CreateInstallment(asOfDate.AddDays(-5), InstallmentStatus.Overdue, 3000m),
                    CreateInstallment(asOfDate.AddDays(5), InstallmentStatus.Upcoming, 2000m)
                }
            )
        };

        // Act
        var dashboard = _generator.Generate(obligations, asOfDate, Currency.EGP);

        // Assert
        Assert.Equal(2, dashboard.OverdueInstallmentsCount);
        Assert.Equal(8000m, dashboard.TotalOverdueAmount.Amount);
    }

    private static ObligationSnapshot CreateObligationSnapshot(
        Guid obligationId,
        string name,
        decimal principal,
        decimal totalPaid,
        decimal outstanding)
    {
        return new ObligationSnapshot(
            ObligationId: obligationId,
            Name: name,
            ObligationType: "Loan",
            Currency: Currency.EGP,
            Principal: new Money(principal, Currency.EGP),
            TotalPaid: new Money(totalPaid, Currency.EGP),
            OutstandingBalance: new Money(outstanding, Currency.EGP),
            IsClosed: false,
            ClosureDate: null,
            Installments: Array.Empty<InstallmentSnapshot>(),
            Charges: Array.Empty<ComputedCharge>()
        );
    }

    private static ObligationSnapshot CreateObligationSnapshotWithInstallments(
        string name,
        InstallmentSnapshot[] installments)
    {
        return new ObligationSnapshot(
            ObligationId: Guid.NewGuid(),
            Name: name,
            ObligationType: "Loan",
            Currency: Currency.EGP,
            Principal: new Money(100_000m, Currency.EGP),
            TotalPaid: Money.Zero(Currency.EGP),
            OutstandingBalance: new Money(100_000m, Currency.EGP),
            IsClosed: false,
            ClosureDate: null,
            Installments: installments,
            Charges: Array.Empty<ComputedCharge>()
        );
    }

    private static InstallmentSnapshot CreateInstallment(
        DateOnly dueDate,
        InstallmentStatus status,
        decimal amount = 5000m)
    {
        var paidAmount = status == InstallmentStatus.Paid ? amount : 0m;

        return new InstallmentSnapshot(
            InstallmentKey: Guid.NewGuid().ToString(),
            DueDate: dueDate,
            ExpectedAmount: new Money(amount, Currency.EGP),
            PaidAmount: new Money(paidAmount, Currency.EGP),
            Status: status
        );
    }
}
