using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

public sealed class ProjectionContext
{
    public DateOnly AsOfDate { get; }
    public Currency BaseCurrency { get; }

    public ProjectionContext(DateOnly asOfDate, Currency baseCurrency)
    {
        AsOfDate = asOfDate;
        BaseCurrency = baseCurrency;
    }
}
