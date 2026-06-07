namespace DebtManager.Domain.Services.Finance;

public static class OverdueCalculator
{
    // Returns 0 if not overdue. Otherwise positive days overdue.
    public static int DaysOverdue(DateOnly dueDate, DateOnly asOfDate)
    {
        if (asOfDate <= dueDate) return 0;
        return asOfDate.DayNumber - dueDate.DayNumber;
    }
}
