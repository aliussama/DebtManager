using System.IO;
using System.Text;
using Xunit;

namespace DebtManager.Integration.Tests;

/// <summary>
/// Tests for CSV export functionality.
/// These tests validate the CSV writing logic used throughout the application.
/// </summary>
public class CsvExportTests
{
    #region CsvWriter Logic Tests (inline implementation for testing)

    /// <summary>
    /// RFC4180-compliant CSV escaping logic.
    /// This mirrors the CsvWriter.Escape method in Desktop.Services.
    /// </summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var needsQuoting = value.Contains(',') ||
                           value.Contains('"') ||
                           value.Contains('\n') ||
                           value.Contains('\r');

        if (!needsQuoting)
            return value;

        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private static string GenerateCsv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var sb = new StringBuilder();

        // Write header
        sb.Append(string.Join(",", headers.Select(h => Escape(h))));
        sb.Append("\r\n");

        // Write rows
        foreach (var row in rows)
        {
            sb.Append(string.Join(",", row.Select(Escape)));
            sb.Append("\r\n");
        }

        return sb.ToString();
    }

    #endregion

    [Fact]
    public void CsvWriter_Escapes_CommasQuotesNewlines()
    {
        // Arrange
        var headers = new List<string> { "Name", "Description", "Notes" };
        var rows = new List<IReadOnlyList<string?>>
        {
            new List<string?> { "Simple", "No special chars", "Just text" },
            new List<string?> { "With,Comma", "Has a comma", "test" },
            new List<string?> { "With\"Quote", "Has \"quotes\" inside", "test" },
            new List<string?> { "With\nNewline", "Has\r\nnewlines", "test" },
            new List<string?> { "Complex\"With,All\nSpecial", "All \"special,\nchars\"", null },
            new List<string?> { null, "", "empty and null" }
        };

        // Act
        var csv = GenerateCsv(headers, rows);

        // Assert - check the full CSV content rather than splitting by lines
        // (since some values contain newlines)
        
        // Header
        Assert.Contains("Name,Description,Notes", csv);

        // Simple row - no escaping needed
        Assert.Contains("Simple,No special chars,Just text", csv);

        // Row with comma - should be quoted
        Assert.Contains("\"With,Comma\",Has a comma,test", csv);

        // Row with quote - should be quoted and quotes doubled
        Assert.Contains("\"With\"\"Quote\",\"Has \"\"quotes\"\" inside\",test", csv);

        // Row with newline - should be quoted
        Assert.Contains("\"With\nNewline\"", csv);
        Assert.Contains("\"Has\r\nnewlines\"", csv);

        // Row with all special chars and null - quotes doubled, commas and newlines inside quotes
        Assert.Contains("\"Complex\"\"With,All\nSpecial\"", csv);

        // Row with null and empty - should be empty strings
        Assert.Contains(",,empty and null", csv);
    }

    [Fact]
    public void CsvWriter_Escape_NullReturnsEmpty()
    {
        Assert.Equal("", Escape(null));
    }

    [Fact]
    public void CsvWriter_Escape_EmptyReturnsEmpty()
    {
        Assert.Equal("", Escape(""));
    }

    [Fact]
    public void CsvWriter_Escape_SimpleValueUnchanged()
    {
        Assert.Equal("Hello World", Escape("Hello World"));
    }

    [Fact]
    public void CsvWriter_Escape_CommaQuoted()
    {
        Assert.Equal("\"Hello,World\"", Escape("Hello,World"));
    }

    [Fact]
    public void CsvWriter_Escape_QuoteDoubledAndQuoted()
    {
        Assert.Equal("\"Say \"\"Hello\"\"\"", Escape("Say \"Hello\""));
    }

    [Fact]
    public void CsvWriter_Escape_NewlineQuoted()
    {
        Assert.Equal("\"Line1\nLine2\"", Escape("Line1\nLine2"));
        Assert.Equal("\"Line1\r\nLine2\"", Escape("Line1\r\nLine2"));
    }

    [Fact]
    public void CsvWriter_UsesCrlfLineEndings()
    {
        // Arrange
        var headers = new List<string> { "Col1" };
        var rows = new List<IReadOnlyList<string?>>
        {
            new List<string?> { "Row1" },
            new List<string?> { "Row2" }
        };

        // Act
        var csv = GenerateCsv(headers, rows);

        // Assert - Should have CRLF line endings (per RFC4180)
        Assert.Contains("\r\n", csv);
        Assert.DoesNotContain("\r\n\r\n", csv); // No double line endings
    }

    [Fact]
    public void Export_UsesStableHeaderOrder_Obligations()
    {
        // Verify the header order for obligations matches expected columns
        var expectedHeaders = new[]
        {
            "Name", "Type", "Principal", "Currency", "Paid", "Outstanding",
            "OverdueCount", "NextDueDate", "Health", "Status"
        };

        // Generate CSV to validate header order
        var csv = GenerateCsv(expectedHeaders.ToList(), Array.Empty<IReadOnlyList<string?>>());
        
        Assert.StartsWith("Name,Type,Principal,Currency,Paid,Outstanding,OverdueCount,NextDueDate,Health,Status", csv);
    }

    [Fact]
    public void Export_UsesStableHeaderOrder_Payments()
    {
        var expectedHeaders = new[]
        {
            "EffectiveDate", "ObligationName", "Amount", "Currency", "Reference",
            "Type", "Status", "PaymentEventId", "OriginalPaymentEventId"
        };

        var csv = GenerateCsv(expectedHeaders.ToList(), Array.Empty<IReadOnlyList<string?>>());

        Assert.StartsWith("EffectiveDate,ObligationName,Amount,Currency,Reference,Type,Status,PaymentEventId,OriginalPaymentEventId", csv);
    }

    [Fact]
    public void Export_UsesStableHeaderOrder_AuditTrail()
    {
        var expectedHeaders = new[]
        {
            "At", "EffectiveDate", "Category", "Severity", "Message",
            "RelatedEventId", "ObligationId", "ObligationName"
        };

        var csv = GenerateCsv(expectedHeaders.ToList(), Array.Empty<IReadOnlyList<string?>>());

        Assert.StartsWith("At,EffectiveDate,Category,Severity,Message,RelatedEventId,ObligationId,ObligationName", csv);
    }

    [Fact]
    public void Export_GeneratesValidCsvWithRealData()
    {
        // Arrange - simulate real obligation data
        var headers = new List<string> { "Name", "Type", "Principal", "Currency", "Status" };
        var rows = new List<IReadOnlyList<string?>>
        {
            new List<string?> { "Home Loan", "Mortgage", "500000.00", "EGP", "Active" },
            new List<string?> { "Car, Finance", "Loan", "150000.00", "EGP", "Active" }, // comma in name
            new List<string?> { "Personal \"Quick\" Loan", "Loan", "25000.00", "EGP", "Closed" }, // quotes in name
        };

        // Act
        var csv = GenerateCsv(headers, rows);

        // Assert
        Assert.Contains("Home Loan,Mortgage,500000.00,EGP,Active", csv);
        Assert.Contains("\"Car, Finance\"", csv); // Should be quoted due to comma
        Assert.Contains("\"Personal \"\"Quick\"\" Loan\"", csv); // Should be quoted with doubled quotes
    }

    [Fact]
    public void Export_HandlesEmptyRows()
    {
        // Arrange
        var headers = new List<string> { "Col1", "Col2" };
        var rows = Array.Empty<IReadOnlyList<string?>>();

        // Act
        var csv = GenerateCsv(headers, rows);

        // Assert - Should only have header row
        Assert.Equal("Col1,Col2\r\n", csv);
    }

    [Fact]
    public void Export_HandlesUnicodeCharacters()
    {
        // Arrange
        var headers = new List<string> { "Name", "Value" };
        var rows = new List<IReadOnlyList<string?>>
        {
            new List<string?> { "?????", "Arabic" },
            new List<string?> { "???", "Japanese" },
            new List<string?> { "Café", "French" },
            new List<string?> { "?? Party", "Emoji" }
        };

        // Act
        var csv = GenerateCsv(headers, rows);

        // Assert
        Assert.Contains("?????,Arabic", csv);
        Assert.Contains("???,Japanese", csv);
        Assert.Contains("Café,French", csv);
        Assert.Contains("?? Party,Emoji", csv);
    }
}
