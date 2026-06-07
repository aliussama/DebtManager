using DebtManager.Domain.Projections;

namespace DebtManager.Domain.Tax;

/// <summary>
/// Result of classifying an item for tax purposes.
/// </summary>
public sealed record TaxClassification(
    string TaxCategory,
    string Confidence,
    string Reason
);

/// <summary>
/// Deterministic tax classifier.
/// Priority: explicit confirmation > rule match > default.
/// </summary>
public static class TaxClassifier
{
    /// <summary>
    /// Classify a cash ledger item.
    /// </summary>
    public static TaxClassification ClassifyCashItem(
        string sourceId,
        string direction,
        string category,
        string reference,
        TaxState taxState)
    {
        // 1) Explicit confirmation
        var key = ("CashLedger", sourceId);
        if (taxState.ConfirmedClassifications.TryGetValue(key, out var confirmed))
            return new TaxClassification(confirmed, "High", "User confirmed");

        // 2) Rule match
        if (direction == "Out")
        {
            var byCategory = FindRule(taxState, "ExpenseCategory", category);
            if (byCategory != null)
                return new TaxClassification(byCategory, "Medium", $"Rule: ExpenseCategory='{category}'");
        }
        else if (direction == "In")
        {
            var bySource = FindRule(taxState, "IncomeSource", reference);
            if (bySource != null)
                return new TaxClassification(bySource, "Medium", $"Rule: IncomeSource='{reference}'");
        }

        // 3) Default
        if (direction == "Out")
            return new TaxClassification(TaxCategories.Unclassified, "Low", "No rule or confirmation; expense");

        if (direction == "In")
            return new TaxClassification(TaxCategories.OtherIncome, "Low", "Default: income");

        return new TaxClassification(TaxCategories.Unclassified, "Low", "Unknown direction");
    }

    /// <summary>
    /// Classify an investment transaction.
    /// </summary>
    public static TaxClassification ClassifyInvestmentTransaction(
        string sourceId,
        string transactionType,
        string symbol,
        TaxState taxState)
    {
        // 1) Explicit confirmation
        var key = ("Investment", sourceId);
        if (taxState.ConfirmedClassifications.TryGetValue(key, out var confirmed))
            return new TaxClassification(confirmed, "High", "User confirmed");

        // 2) Rule match by symbol
        var bySymbol = FindRule(taxState, "Symbol", symbol);
        if (bySymbol != null)
            return new TaxClassification(bySymbol, "Medium", $"Rule: Symbol='{symbol}'");

        // 2b) Rule match by transaction type
        var byType = FindRule(taxState, "TransactionType", transactionType);
        if (byType != null)
            return new TaxClassification(byType, "Medium", $"Rule: TransactionType='{transactionType}'");

        // 3) Default by transaction type
        return transactionType switch
        {
            "Dividend" => new TaxClassification(TaxCategories.DividendIncome, "Low", "Default: Dividend"),
            "Interest" => new TaxClassification(TaxCategories.InterestIncome, "Low", "Default: Interest"),
            "Sell" or "TransferOut" => new TaxClassification(TaxCategories.CapitalGain, "Low", "Default: Sell"),
            _ => new TaxClassification(TaxCategories.Unclassified, "Low", $"No default for '{transactionType}'")
        };
    }

    private static string? FindRule(TaxState taxState, string appliesTo, string matchValue)
    {
        if (string.IsNullOrEmpty(matchValue))
            return null;

        var rule = taxState.ActiveRules.FirstOrDefault(r =>
            string.Equals(r.AppliesTo, appliesTo, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.MatchValue, matchValue, StringComparison.OrdinalIgnoreCase));

        return rule?.TaxCategory;
    }
}
