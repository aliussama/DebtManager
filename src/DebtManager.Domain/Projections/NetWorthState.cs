namespace DebtManager.Domain.Projections;

/// <summary>
/// A row in the net worth breakdown.
/// </summary>
public sealed class NetWorthBreakdownRow
{
    public string Category { get; set; } = string.Empty;
    public string SubCategory { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }
    public string NativeCurrencyCode { get; set; } = string.Empty;
    public decimal NativeAmount { get; set; }
    public string ReportingCurrencyCode { get; set; } = string.Empty;
    public decimal ReportingAmount { get; set; }
    public bool IsValued { get; set; } = true;
    public string ValuationNote { get; set; } = string.Empty;
}

/// <summary>
/// Full net worth state derived deterministically from events.
/// </summary>
public sealed class NetWorthState
{
    public DateOnly AsOfDate { get; set; }
    public string ReportingCurrency { get; set; } = string.Empty;
    public decimal TotalAssets { get; set; }
    public decimal TotalCash { get; set; }
    public decimal TotalInvestmentAssets { get; set; }
    public decimal TotalLiabilities { get; set; }
    public decimal KnownNetWorth { get; set; }
    public int UnknownValueCount { get; set; }
    public List<NetWorthBreakdownRow> Rows { get; } = new();
}
