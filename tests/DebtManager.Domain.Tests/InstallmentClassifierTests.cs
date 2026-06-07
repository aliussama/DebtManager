using DebtManager.Domain.Projections.Installments;

namespace DebtManager.Domain.Tests;

public class InstallmentClassifierTests
{
    [Fact]
    public void Upcoming_WhenBeforeDueDate()
    {
        var c = new InstallmentClassifier(nearDueWindowDays: 7);

        var (status, daysOverdue, risk) = c.Classify(
            dueDate: new DateOnly(2026, 9, 15),
            isFullyPaid: false,
            asOfDate: new DateOnly(2026, 9, 1));

        Assert.Equal(InstallmentStatus.Upcoming, status);
        Assert.Equal(0, daysOverdue);
        Assert.Equal(InstallmentRisk.None, risk);
    }

    [Fact]
    public void NearDue_WhenWithinWindow()
    {
        var c = new InstallmentClassifier(nearDueWindowDays: 7);

        var (status, _, risk) = c.Classify(
            dueDate: new DateOnly(2026, 9, 15),
            isFullyPaid: false,
            asOfDate: new DateOnly(2026, 9, 10)); // 5 days before

        Assert.Equal(InstallmentStatus.Upcoming, status);
        Assert.True(risk.HasFlag(InstallmentRisk.NearDue));
    }

    [Fact]
    public void DueToday_OnDueDate()
    {
        var c = new InstallmentClassifier();

        var (status, daysOverdue, risk) = c.Classify(
            dueDate: new DateOnly(2026, 9, 15),
            isFullyPaid: false,
            asOfDate: new DateOnly(2026, 9, 15));

        Assert.Equal(InstallmentStatus.DueToday, status);
        Assert.Equal(0, daysOverdue);
        Assert.True(risk.HasFlag(InstallmentRisk.NearDue));
    }

    [Fact]
    public void Overdue_WhenAfterDueDate()
    {
        var c = new InstallmentClassifier();

        var (status, daysOverdue, risk) = c.Classify(
            dueDate: new DateOnly(2026, 9, 15),
            isFullyPaid: false,
            asOfDate: new DateOnly(2026, 9, 20));

        Assert.Equal(InstallmentStatus.Overdue, status);
        Assert.Equal(5, daysOverdue);
        Assert.True(risk.HasFlag(InstallmentRisk.PenaltyLikely));
    }

    [Fact]
    public void Paid_WhenFullyPaid()
    {
        var c = new InstallmentClassifier();

        var (status, daysOverdue, risk) = c.Classify(
            dueDate: new DateOnly(2026, 9, 15),
            isFullyPaid: true,
            asOfDate: new DateOnly(2026, 12, 31));

        Assert.Equal(InstallmentStatus.Paid, status);
        Assert.Equal(0, daysOverdue);
        Assert.Equal(InstallmentRisk.None, risk);
    }
}
