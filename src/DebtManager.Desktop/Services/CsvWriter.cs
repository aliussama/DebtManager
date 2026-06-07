using System.IO;
using System.Text;

namespace DebtManager.Desktop.Services;

/// <summary>
/// RFC4180-compliant CSV writer utility.
/// Handles escaping of commas, quotes, and newlines.
/// </summary>
public static class CsvWriter
{
    /// <summary>
    /// Escapes a value for CSV output according to RFC4180.
    /// - Null values become empty string
    /// - Values containing comma, quote, or newline are wrapped in quotes
    /// - Internal quotes are doubled
    /// </summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // Check if the value needs quoting
        var needsQuoting = value.Contains(',') ||
                           value.Contains('"') ||
                           value.Contains('\n') ||
                           value.Contains('\r');

        if (!needsQuoting)
            return value;

        // Escape internal quotes by doubling them
        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    /// <summary>
    /// Writes CSV data to a TextWriter.
    /// Always writes headers first, then rows.
    /// Uses CRLF line endings per RFC4180.
    /// </summary>
    public static void Write(TextWriter writer, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        // Write header row
        var headerList = new List<string?>(headers.Count);
        foreach (var h in headers)
        {
            headerList.Add(h);
        }
        WriteRow(writer, headerList);

        // Write data rows
        foreach (var row in rows)
        {
            WriteRow(writer, row);
        }
    }

    /// <summary>
    /// Writes a single CSV row.
    /// </summary>
    private static void WriteRow(TextWriter writer, IReadOnlyList<string?> values)
    {
        var escapedValues = values.Select(Escape);
        writer.Write(string.Join(",", escapedValues));
        writer.Write("\r\n"); // CRLF per RFC4180
    }

    /// <summary>
    /// Generates complete CSV content as a string.
    /// Useful for testing.
    /// </summary>
    public static string Generate(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        using var writer = new StringWriter();
        Write(writer, headers, rows);
        return writer.ToString();
    }
}
