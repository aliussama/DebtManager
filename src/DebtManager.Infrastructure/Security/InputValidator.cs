using System.Text.RegularExpressions;

namespace DebtManager.Infrastructure.Security;

/// <summary>
/// Input validation and sanitization for financial applications.
/// </summary>
public static partial class InputValidator
{
    // Maximum lengths for common fields
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MaxNotesLength = 5000;
    public const int MaxJsonPayloadLength = 100_000; // 100KB
    public const int MaxRulePackJsonLength = 500_000; // 500KB

    // Amount limits (configurable per institution)
    public const decimal MaxPaymentAmount = 100_000_000m; // 100 million
    public const decimal MinPaymentAmount = 0.01m;

    /// <summary>
    /// Validate a monetary amount.
    /// </summary>
    public static ValidationResult ValidateAmount(decimal amount, string fieldName = "Amount")
    {
        if (amount < 0)
            return ValidationResult.Fail($"{fieldName} cannot be negative.");

        if (amount < MinPaymentAmount && amount != 0)
            return ValidationResult.Fail($"{fieldName} must be at least {MinPaymentAmount}.");

        if (amount > MaxPaymentAmount)
            return ValidationResult.Fail($"{fieldName} exceeds maximum allowed ({MaxPaymentAmount:N2}).");

        // Check for too many decimal places (max 4 for most currencies)
        var decimalPlaces = BitConverter.GetBytes(decimal.GetBits(amount)[3])[2];
        if (decimalPlaces > 4)
            return ValidationResult.Fail($"{fieldName} has too many decimal places (max 4).");

        return ValidationResult.Ok();
    }

    /// <summary>
    /// Validate a name field.
    /// </summary>
    public static ValidationResult ValidateName(string? name, string fieldName = "Name", bool required = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            return required ? ValidationResult.Fail($"{fieldName} is required.") : ValidationResult.Ok();

        if (name.Length > MaxNameLength)
            return ValidationResult.Fail($"{fieldName} exceeds maximum length ({MaxNameLength}).");

        // Check for potentially dangerous characters
        if (ContainsSqlInjectionPatterns(name))
            return ValidationResult.Fail($"{fieldName} contains invalid characters.");

        return ValidationResult.Ok();
    }

    /// <summary>
    /// Validate a date is reasonable for financial data.
    /// </summary>
    public static ValidationResult ValidateDate(DateOnly date, string fieldName = "Date")
    {
        var minDate = new DateOnly(1900, 1, 1);
        var maxDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(100));

        if (date < minDate)
            return ValidationResult.Fail($"{fieldName} is too far in the past.");

        if (date > maxDate)
            return ValidationResult.Fail($"{fieldName} is too far in the future.");

        return ValidationResult.Ok();
    }

    /// <summary>
    /// Validate a GUID is not empty.
    /// </summary>
    public static ValidationResult ValidateGuid(Guid id, string fieldName = "Id")
    {
        if (id == Guid.Empty)
            return ValidationResult.Fail($"{fieldName} cannot be empty.");

        return ValidationResult.Ok();
    }

    /// <summary>
    /// Validate a currency code.
    /// </summary>
    public static ValidationResult ValidateCurrencyCode(string? code, string fieldName = "CurrencyCode")
    {
        if (string.IsNullOrWhiteSpace(code))
            return ValidationResult.Fail($"{fieldName} is required.");

        if (code.Length != 3)
            return ValidationResult.Fail($"{fieldName} must be a 3-letter ISO code.");

        if (!CurrencyCodeRegex().IsMatch(code))
            return ValidationResult.Fail($"{fieldName} must contain only letters.");

        return ValidationResult.Ok();
    }

    /// <summary>
    /// Validate JSON payload size.
    /// </summary>
    public static ValidationResult ValidateJsonPayload(string? json, int maxLength = MaxJsonPayloadLength)
    {
        if (string.IsNullOrEmpty(json))
            return ValidationResult.Ok();

        if (json.Length > maxLength)
            return ValidationResult.Fail($"JSON payload exceeds maximum size ({maxLength} characters).");

        return ValidationResult.Ok();
    }

    /// <summary>
    /// Validate an interest rate.
    /// </summary>
    public static ValidationResult ValidateInterestRate(decimal rate, string fieldName = "Interest rate")
    {
        if (rate < 0)
            return ValidationResult.Fail($"{fieldName} cannot be negative.");

        if (rate > 1) // 100%
            return ValidationResult.Fail($"{fieldName} exceeds 100%. Please enter as decimal (e.g., 0.12 for 12%).");

        return ValidationResult.Ok();
    }

    /// <summary>
    /// Validate installment count.
    /// </summary>
    public static ValidationResult ValidateInstallmentCount(int count, string fieldName = "Installment count")
    {
        if (count < 1)
            return ValidationResult.Fail($"{fieldName} must be at least 1.");

        if (count > 600) // 50 years of monthly payments
            return ValidationResult.Fail($"{fieldName} exceeds maximum (600).");

        return ValidationResult.Ok();
    }

    /// <summary>
    /// Sanitize a string for safe storage.
    /// </summary>
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // Remove null bytes and control characters (except newlines/tabs)
        var sanitized = ControlCharRegex().Replace(input, "");

        // Trim whitespace
        return sanitized.Trim();
    }

    /// <summary>
    /// Check for SQL injection patterns.
    /// Note: Parameterized queries are the primary defense. This is defense-in-depth.
    /// </summary>
    private static bool ContainsSqlInjectionPatterns(string input)
    {
        // Only catch obvious attack patterns - parameterized queries are the real protection
        // Allow single quotes for names like "O'Brien" or "John's Company"
        var patterns = new[]
        {
            @"(--|;|/\*|\*/|xp_|sp_)",  // SQL comment/command indicators
            @"(\bexec\s|\bexecute\s)",   // EXEC commands
            @"(\bselect\s+.+\bfrom\b)",  // SELECT FROM
            @"(\binsert\s+.+\binto\b)",  // INSERT INTO
            @"(\bupdate\s+.+\bset\b)",   // UPDATE SET
            @"(\bdelete\s+.+\bfrom\b)",  // DELETE FROM
            @"(\bdrop\s+\w+)",           // DROP
            @"(\balter\s+\w+)",          // ALTER
            @"(\btruncate\s+\w+)",       // TRUNCATE
        };

        foreach (var pattern in patterns)
        {
            if (Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }

    [GeneratedRegex("^[A-Za-z]{3}$")]
    private static partial Regex CurrencyCodeRegex();

    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F]")]
    private static partial Regex ControlCharRegex();
}

/// <summary>
/// Result of input validation.
/// </summary>
public sealed record ValidationResult(bool IsValid, string? ErrorMessage)
{
    public static ValidationResult Ok() => new(true, null);
    public static ValidationResult Fail(string message) => new(false, message);

    public void ThrowIfInvalid()
    {
        if (!IsValid)
            throw new ArgumentException(ErrorMessage);
    }
}

/// <summary>
/// Builder for validating multiple fields.
/// </summary>
public sealed class ValidationBuilder
{
    private readonly List<ValidationResult> _results = new();

    public ValidationBuilder Validate(ValidationResult result)
    {
        _results.Add(result);
        return this;
    }

    public ValidationBuilder ValidateAmount(decimal amount, string fieldName = "Amount")
    {
        return Validate(InputValidator.ValidateAmount(amount, fieldName));
    }

    public ValidationBuilder ValidateName(string? name, string fieldName = "Name", bool required = true)
    {
        return Validate(InputValidator.ValidateName(name, fieldName, required));
    }

    public ValidationBuilder ValidateGuid(Guid id, string fieldName = "Id")
    {
        return Validate(InputValidator.ValidateGuid(id, fieldName));
    }

    public ValidationBuilder ValidateDate(DateOnly date, string fieldName = "Date")
    {
        return Validate(InputValidator.ValidateDate(date, fieldName));
    }

    public ValidationBuilder ValidateCurrencyCode(string? code, string fieldName = "CurrencyCode")
    {
        return Validate(InputValidator.ValidateCurrencyCode(code, fieldName));
    }

    public ValidationResult Build()
    {
        var errors = _results
            .Where(r => !r.IsValid)
            .Select(r => r.ErrorMessage)
            .ToList();

        return errors.Any()
            ? ValidationResult.Fail(string.Join(" ", errors))
            : ValidationResult.Ok();
    }

    public void ThrowIfInvalid()
    {
        Build().ThrowIfInvalid();
    }
}
