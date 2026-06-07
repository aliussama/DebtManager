using System.Globalization;
using System.Text;

namespace DebtManager.Domain.Import;

/// <summary>
/// Canonical representation of a parsed bank transaction row.
/// </summary>
public sealed record BankCsvRow(
    DateOnly TxnDate,
    decimal Amount,
    string CurrencyCode,
    string Description,
    string Reference,
    string Counterparty,
    string Direction, // "credit" or "debit"
    string RawLine
);

/// <summary>
/// Pure deterministic CSV parser. Produces canonical rows from CSV text + profile mapping.
/// </summary>
public static class BankCsvParser
{
    public static IReadOnlyList<BankCsvRow> Parse(string csvContent, BankImportProfile profile)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
            return Array.Empty<BankCsvRow>();

        var lines = SplitLines(csvContent);
        var delimiter = DetectDelimiter(csvContent, profile.Delimiter);
        var results = new List<BankCsvRow>();

        var startIndex = profile.HasHeaderRow ? 1 : 0;

        for (int i = startIndex; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = ParseCsvLine(line, delimiter);
            var row = MapRow(fields, profile, line);
            if (row != null)
                results.Add(row);
        }

        return results;
    }

    public static string DetectDelimiter(string csvContent, string defaultDelimiter)
    {
        var firstLine = csvContent.Split('\n').FirstOrDefault()?.Trim() ?? "";
        if (string.IsNullOrEmpty(firstLine)) return defaultDelimiter;

        var commaCount = firstLine.Count(c => c == ',');
        var semiCount = firstLine.Count(c => c == ';');
        var tabCount = firstLine.Count(c => c == '\t');

        if (semiCount > commaCount && semiCount > tabCount) return ";";
        if (tabCount > commaCount && tabCount > semiCount) return "\t";
        if (commaCount > 0) return ",";

        return defaultDelimiter;
    }

    private static IReadOnlyList<string> SplitLines(string content)
    {
        return content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
    }

    internal static IReadOnlyList<string> ParseCsvLine(string line, string delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        char delim = delimiter.Length == 1 ? delimiter[0] : ',';

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == delim)
                {
                    fields.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }
        fields.Add(current.ToString().Trim());
        return fields;
    }

    private static BankCsvRow? MapRow(IReadOnlyList<string> fields, BankImportProfile profile, string rawLine)
    {
        try
        {
            // Parse date
            if (profile.DateColumn >= fields.Count) return null;
            var dateStr = fields[profile.DateColumn].Trim().Trim('"');
            if (!DateOnly.TryParseExact(dateStr, profile.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var txnDate))
            {
                if (!DateOnly.TryParse(dateStr, CultureInfo.InvariantCulture, out txnDate))
                    return null;
            }

            // Parse amount + direction
            decimal amount;
            string direction;

            if (profile.SignConvention == "separate_columns" &&
                profile.CreditColumn.HasValue && profile.DebitColumn.HasValue)
            {
                var creditStr = SafeField(fields, profile.CreditColumn.Value);
                var debitStr = SafeField(fields, profile.DebitColumn.Value);

                var creditAmt = ParseDecimal(creditStr, profile.DecimalSeparator);
                var debitAmt = ParseDecimal(debitStr, profile.DecimalSeparator);

                if (creditAmt > 0)
                {
                    amount = creditAmt;
                    direction = "credit";
                }
                else
                {
                    amount = debitAmt > 0 ? debitAmt : 0;
                    direction = "debit";
                }
            }
            else
            {
                if (profile.AmountColumn >= fields.Count) return null;
                var amountStr = fields[profile.AmountColumn].Trim().Trim('"');
                amount = ParseDecimal(amountStr, profile.DecimalSeparator);

                if (profile.SignConvention == "negative_is_debit")
                {
                    direction = amount < 0 ? "debit" : "credit";
                    amount = Math.Abs(amount);
                }
                else if (profile.SignConvention == "positive_is_debit")
                {
                    direction = amount > 0 ? "debit" : "credit";
                    amount = Math.Abs(amount);
                }
                else
                {
                    direction = amount < 0 ? "debit" : "credit";
                    amount = Math.Abs(amount);
                }
            }

            if (amount == 0) return null;

            var description = SafeField(fields, profile.DescriptionColumn);
            var reference = SafeField(fields, profile.ReferenceColumn);
            var counterparty = SafeField(fields, profile.CounterpartyColumn);

            return new BankCsvRow(txnDate, amount, profile.CurrencyCode, description, reference, counterparty, direction, rawLine);
        }
        catch
        {
            return null;
        }
    }

    private static string SafeField(IReadOnlyList<string> fields, int? columnIndex)
    {
        if (!columnIndex.HasValue || columnIndex.Value >= fields.Count) return string.Empty;
        return fields[columnIndex.Value].Trim().Trim('"');
    }

    private static decimal ParseDecimal(string value, string decimalSeparator)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0m;

        value = value.Trim();

        if (decimalSeparator == ",")
        {
            value = value.Replace(".", "").Replace(",", ".");
        }
        else
        {
            value = value.Replace(",", "");
        }

        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0m;
    }
}
