namespace DebtManager.Domain.Tax;

/// <summary>
/// Computes the tax year date range given a profile's start month/day and a tax year number.
/// </summary>
public static class TaxYear
{
    /// <summary>
    /// Gets the inclusive [start, end] date range for a tax year.
    /// For a standard Jan-1 profile, tax year 2025 = 2025-01-01 to 2025-12-31.
    /// For a Apr-1 profile, tax year 2025 = 2025-04-01 to 2026-03-31.
    /// </summary>
    public static (DateOnly Start, DateOnly End) GetRange(int taxYear, int startMonth, int startDay)
    {
        var start = new DateOnly(taxYear, startMonth, startDay);
        var end = start.AddYears(1).AddDays(-1);
        return (start, end);
    }

    /// <summary>
    /// Determines which tax year a given date falls into.
    /// </summary>
    public static int GetTaxYear(DateOnly date, int startMonth, int startDay)
    {
        var candidateYear = date.Year;
        var candidateStart = new DateOnly(candidateYear, startMonth, startDay);

        if (date >= candidateStart)
            return candidateYear;

        return candidateYear - 1;
    }
}
