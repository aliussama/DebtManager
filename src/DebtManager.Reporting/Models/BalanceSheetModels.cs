namespace DebtManager.Reporting.Models;

/// <summary>
/// Immutable balance sheet report showing Assets, Liabilities, and Equity.
/// All financial logic computed in domain/reporting layer.
/// No UI types. No formatting. No randomness.
/// </summary>
public sealed record BalanceSheetReport(
    IReadOnlyList<BalanceSheetItem> Assets,
    IReadOnlyList<BalanceSheetItem> Liabilities,
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal Equity,
    int UnknownExcludedCount,
    DateOnly AsOfDate,
    string ReportingCurrency
);

/// <summary>
/// A single line item in the balance sheet (asset or liability).
/// </summary>
public sealed record BalanceSheetItem(
    Guid? EntityId,
    string Name,
    string Category,
    decimal Amount,
    string CurrencyCode
);
