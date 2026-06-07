using DebtManager.Infrastructure.Security;
using Xunit;

namespace DebtManager.Domain.Tests.Security;

public class InputValidatorTests
{
    [Theory]
    [InlineData(100, true)]
    [InlineData(0, true)]
    [InlineData(0.01, true)]
    [InlineData(100_000_000, true)]
    [InlineData(-1, false)]
    [InlineData(100_000_001, false)]
    public void ValidateAmount_ReturnsExpectedResult(decimal amount, bool expectedValid)
    {
        // Act
        var result = InputValidator.ValidateAmount(amount);

        // Assert
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void ValidateAmount_TooManyDecimalPlaces_Fails()
    {
        // Arrange
        var amount = 100.123456789m;

        // Act
        var result = InputValidator.ValidateAmount(amount);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("decimal places", result.ErrorMessage);
    }

    [Theory]
    [InlineData("John Doe", true)]
    [InlineData("???? ????", true)] // Arabic name
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ValidateName_RequiredField_ReturnsExpectedResult(string? name, bool expectedValid)
    {
        // Act
        var result = InputValidator.ValidateName(name, "Name", required: true);

        // Assert
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void ValidateName_OptionalField_AllowsEmpty()
    {
        // Act
        var result = InputValidator.ValidateName(null, "Notes", required: false);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateName_ExceedsMaxLength_Fails()
    {
        // Arrange
        var longName = new string('A', 201);

        // Act
        var result = InputValidator.ValidateName(longName);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("maximum length", result.ErrorMessage);
    }

    [Theory]
    [InlineData("SELECT * FROM users", false)] // SQL injection attempt
    [InlineData("Normal Name", true)]
    [InlineData("John's Company", true)] // Single apostrophe is OK in isolation
    public void ValidateName_SqlInjection_IsDetected(string input, bool expectedValid)
    {
        // Act
        var result = InputValidator.ValidateName(input);

        // Assert
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("EGP", true)]
    [InlineData("USD", true)]
    [InlineData("egp", true)] // Case insensitive check
    [InlineData("EURO", false)] // 4 chars
    [InlineData("EU", false)] // 2 chars
    [InlineData("123", false)] // Numbers
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ValidateCurrencyCode_ReturnsExpectedResult(string? code, bool expectedValid)
    {
        // Act
        var result = InputValidator.ValidateCurrencyCode(code);

        // Assert
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData(0.12, true)] // 12%
    [InlineData(0, true)] // 0%
    [InlineData(1, true)] // 100%
    [InlineData(-0.01, false)] // Negative
    [InlineData(1.5, false)] // 150% - probably a mistake
    public void ValidateInterestRate_ReturnsExpectedResult(decimal rate, bool expectedValid)
    {
        // Act
        var result = InputValidator.ValidateInterestRate(rate);

        // Assert
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(12, true)] // 1 year
    [InlineData(360, true)] // 30 years
    [InlineData(600, true)] // 50 years (max)
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(601, false)]
    public void ValidateInstallmentCount_ReturnsExpectedResult(int count, bool expectedValid)
    {
        // Act
        var result = InputValidator.ValidateInstallmentCount(count);

        // Assert
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void ValidateDate_ReasonableRange_Passes()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Act & Assert
        Assert.True(InputValidator.ValidateDate(today).IsValid);
        Assert.True(InputValidator.ValidateDate(new DateOnly(2000, 1, 1)).IsValid);
        Assert.True(InputValidator.ValidateDate(new DateOnly(2050, 12, 31)).IsValid);
    }

    [Fact]
    public void ValidateDate_TooOld_Fails()
    {
        // Arrange
        var oldDate = new DateOnly(1800, 1, 1);

        // Act
        var result = InputValidator.ValidateDate(oldDate);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("past", result.ErrorMessage);
    }

    [Fact]
    public void ValidateGuid_Empty_Fails()
    {
        // Act
        var result = InputValidator.ValidateGuid(Guid.Empty);

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateGuid_Valid_Passes()
    {
        // Act
        var result = InputValidator.ValidateGuid(Guid.NewGuid());

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateJsonPayload_ExceedsLimit_Fails()
    {
        // Arrange
        var largeJson = new string('X', 100_001);

        // Act
        var result = InputValidator.ValidateJsonPayload(largeJson);

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Sanitize_RemovesControlCharacters()
    {
        // Arrange
        var input = "Hello\x00World\x01!";

        // Act
        var sanitized = InputValidator.Sanitize(input);

        // Assert
        Assert.Equal("HelloWorld!", sanitized);
    }

    [Fact]
    public void Sanitize_PreservesNewlinesAndTabs()
    {
        // Arrange
        var input = "Line1\nLine2\tTabbed";

        // Act
        var sanitized = InputValidator.Sanitize(input);

        // Assert
        Assert.Contains("\n", sanitized);
        Assert.Contains("\t", sanitized);
    }

    [Fact]
    public void ValidationBuilder_MultipleErrors_AggregatesMessages()
    {
        // Arrange & Act
        var result = new ValidationBuilder()
            .ValidateAmount(-100)
            .ValidateName("")
            .ValidateGuid(Guid.Empty)
            .Build();

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("negative", result.ErrorMessage);
        Assert.Contains("required", result.ErrorMessage);
        Assert.Contains("empty", result.ErrorMessage);
    }

    [Fact]
    public void ValidationBuilder_AllValid_ReturnsOk()
    {
        // Arrange & Act
        var result = new ValidationBuilder()
            .ValidateAmount(1000)
            .ValidateName("John Doe")
            .ValidateGuid(Guid.NewGuid())
            .ValidateDate(DateOnly.FromDateTime(DateTime.UtcNow))
            .Build();

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidationBuilder_ThrowIfInvalid_ThrowsOnError()
    {
        // Arrange
        var builder = new ValidationBuilder()
            .ValidateAmount(-100);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => builder.ThrowIfInvalid());
    }
}
