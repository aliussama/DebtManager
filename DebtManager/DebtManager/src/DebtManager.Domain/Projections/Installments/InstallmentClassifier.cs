namespace DebtManager.Domain.Projections.Installments;

public sealed class InstallmentClassifier
{
    private readonly int _nearDueWindowDays;

    public InstallmentClassifier(int nearDueWindowDays = 7)
    {
        if (nearDueWindowDays < 0) throw new ArgumentOutOfRangeException(nameof(nearDueWindowDays));
        _nearDueWindowDays = nearDueWindowDays;
    }

    public (InstallmentStatus Status, int DaysOverdue, InstallmentRisk Risk) Classify(
        DateOnly dueDate,
        bool isFullyPaid,
        DateOnly asOfDate)
    {
        if (isFullyPaid)
            return (InstallmentStatus.Paid, 0, InstallmentRisk.None);

        if (asOfDate < dueDate)
        {
            var daysToDue = dueDate.DayNumber - asOfDate.DayNumber;
            var risk = daysToDue <= _nearDueWindowDays ? InstallmentRisk.NearDue : InstallmentRisk.None;
            return (InstallmentStatus.Upcoming, 0, risk);
        }

        if (asOfDate == dueDate)
            return (InstallmentStatus.DueToday, 0, InstallmentRisk.NearDue);

        // overdue
        var daysOverdue = asOfDate.DayNumber - dueDate.DayNumber;
        // PenaltyLikely is placeholder; grace rules will refine this later
        var riskFlags = InstallmentRisk.PenaltyLikely;
        return (InstallmentStatus.Overdue, daysOverdue, riskFlags);
    }
}
