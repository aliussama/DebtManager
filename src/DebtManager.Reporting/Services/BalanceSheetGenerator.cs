using DebtManager.Domain.Projections;
using DebtManager.Reporting.Models;

namespace DebtManager.Reporting.Services;

/// <summary>
/// Pure function: Generates a Balance Sheet from NetWorthState.
/// Groups assets and liabilities by category with deterministic ordering.
/// Excludes unknown values from totals and counts them separately.
/// No DateTime.Now. No static state. No randomness.
/// </summary>
public sealed class BalanceSheetGenerator
{
    /// <summary>
    /// Generate balance sheet from net worth breakdown.
    /// All inputs are projection states loaded deterministically.
    /// </summary>
    public BalanceSheetReport Generate(
        NetWorthState netWorthState,
        DateOnly asOfDate,
        string reportingCurrency)
    {
        var assets = new List<BalanceSheetItem>();
        var liabilities = new List<BalanceSheetItem>();
        var unknownCount = netWorthState.UnknownValueCount;

        // Group rows by category
        foreach (var row in netWorthState.Rows.Where(r => r.IsValued))
        {
            var item = new BalanceSheetItem(
                row.ReferenceId,
                row.Name,
                GetDisplayCategory(row.Category, row.SubCategory),
                row.ReportingAmount,
                row.ReportingCurrencyCode
            );

            if (row.Category == "Cash" || row.Category == "Asset")
            {
                assets.Add(item);
            }
            else if (row.Category == "Liability")
            {
                liabilities.Add(item);
            }
        }

        // Deterministic ordering: Category, then Name
        var orderedAssets = assets
            .OrderBy(a => GetCategoryOrder(a.Category))
            .ThenBy(a => a.Category)
            .ThenBy(a => a.Name)
            .ToList();

        var orderedLiabilities = liabilities
            .OrderBy(l => l.Category)
            .ThenBy(l => l.Name)
            .ToList();

        var totalAssets = netWorthState.TotalAssets;
        var totalLiabilities = netWorthState.TotalLiabilities;
        var equity = totalAssets - totalLiabilities;

        return new BalanceSheetReport(
            orderedAssets,
            orderedLiabilities,
            totalAssets,
            totalLiabilities,
            equity,
            unknownCount,
            asOfDate,
            reportingCurrency
        );
    }

    /// <summary>
    /// Maps raw category + subcategory to display category for balance sheet.
    /// </summary>
    private static string GetDisplayCategory(string category, string subCategory)
    {
        if (category == "Cash")
        {
            return subCategory switch
            {
                "Cash" => "Cash & Cash Equivalents",
                "Bank" or "Checking" or "Savings" => "Bank Accounts",
                _ => "Cash"
            };
        }

        if (category == "Asset")
        {
            return subCategory switch
            {
                "RealEstate" => "Real Estate",
                "Vehicle" => "Vehicles",
                "PreciousMetal" => "Precious Metals",
                "Commodity" => "Commodities",
                "Collectible" => "Collectibles",
                "InvestmentAccount" => "Investment Accounts",
                _ => "Other Assets"
            };
        }

        if (category == "Liability")
        {
            return subCategory switch
            {
                "Loan" => "Loans",
                "Mortgage" => "Mortgages",
                "CreditCard" => "Credit Cards",
                "Tuition" => "Tuition Obligations",
                _ => "Other Liabilities"
            };
        }

        return category;
    }

    /// <summary>
    /// Defines display order for asset categories.
    /// Cash first, then investment accounts, then real estate, etc.
    /// </summary>
    private static int GetCategoryOrder(string category)
    {
        return category switch
        {
            "Cash & Cash Equivalents" => 1,
            "Bank Accounts" => 2,
            "Investment Accounts" => 3,
            "Real Estate" => 4,
            "Vehicles" => 5,
            "Precious Metals" => 6,
            "Commodities" => 7,
            "Collectibles" => 8,
            _ => 99
        };
    }
}
