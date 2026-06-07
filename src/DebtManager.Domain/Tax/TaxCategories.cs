namespace DebtManager.Domain.Tax;

/// <summary>
/// Well-known tax category constants.
/// </summary>
public static class TaxCategories
{
    public const string DividendIncome = "DividendIncome";
    public const string InterestIncome = "InterestIncome";
    public const string CapitalGain = "CapitalGain";
    public const string DeductibleExpense = "DeductibleExpense";
    public const string NonDeductible = "NonDeductible";
    public const string OtherIncome = "OtherIncome";
    public const string Unclassified = "Unclassified";
}
