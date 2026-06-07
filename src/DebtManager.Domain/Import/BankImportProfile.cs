using System.Text.Json;

namespace DebtManager.Domain.Import;

/// <summary>
/// Represents column mapping and parsing configuration for a bank CSV format.
/// Stored as JSON in BankImportProfileCreated.MappingJson.
/// </summary>
public sealed class BankImportProfile
{
    public string Delimiter { get; set; } = ",";
    public bool HasHeaderRow { get; set; } = true;
    public int DateColumn { get; set; }
    public int AmountColumn { get; set; }
    public int? DescriptionColumn { get; set; }
    public int? ReferenceColumn { get; set; }
    public int? CounterpartyColumn { get; set; }
    public int? CreditColumn { get; set; }
    public int? DebitColumn { get; set; }
    public int? DirectionColumn { get; set; }
    public string DateFormat { get; set; } = "yyyy-MM-dd";
    public string DecimalSeparator { get; set; } = ".";
    public string CurrencyCode { get; set; } = "EGP";

    /// <summary>
    /// Sign convention: "negative_is_debit" means negative amounts are expenses.
    /// "positive_is_debit" means positive amounts in a single-amount column are expenses.
    /// "separate_columns" means debit/credit are in separate columns.
    /// </summary>
    public string SignConvention { get; set; } = "negative_is_debit";

    public static BankImportProfile FromJson(string json) =>
        JsonSerializer.Deserialize<BankImportProfile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? new BankImportProfile();

    public string ToJson() =>
        JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}
