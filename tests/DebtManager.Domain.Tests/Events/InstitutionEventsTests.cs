using DebtManager.Domain.Events;
using Xunit;

namespace DebtManager.Domain.Tests.Events;

public class InstitutionEventsTests
{
    [Fact]
    public void FinancialInstitutionRegistered_CanBeCreated_WithValidData()
    {
        // Arrange & Act
        var @event = new FinancialInstitutionRegistered(
            InstitutionId: Guid.NewGuid(),
            Name: "National Bank of Egypt",
            Type: InstitutionType.Bank,
            CountryCode: "EG",
            Metadata: new InstitutionMetadata(
                BranchCode: "NBE001",
                SwiftCode: "NBEGEGCX",
                Website: "https://nbe.com.eg",
                SupportPhone: "+20225946400",
                CustomFields: null
            ),
            EffectiveDate: new DateOnly(2025, 1, 1)
        );

        // Assert
        Assert.Equal("National Bank of Egypt", @event.Name);
        Assert.Equal(InstitutionType.Bank, @event.Type);
        Assert.Equal("EG", @event.CountryCode);
        Assert.NotNull(@event.Metadata);
        Assert.Equal("NBEGEGCX", @event.Metadata.SwiftCode);
    }

    [Theory]
    [InlineData(InstitutionType.Bank)]
    [InlineData(InstitutionType.University)]
    [InlineData(InstitutionType.PropertyDeveloper)]
    [InlineData(InstitutionType.CreditCardIssuer)]
    [InlineData(InstitutionType.Government)]
    public void FinancialInstitutionRegistered_SupportsAllTypes(InstitutionType type)
    {
        // Arrange & Act
        var @event = new FinancialInstitutionRegistered(
            InstitutionId: Guid.NewGuid(),
            Name: "Test Institution",
            Type: type,
            CountryCode: "EG",
            Metadata: null,
            EffectiveDate: new DateOnly(2025, 1, 1)
        );

        // Assert
        Assert.Equal(type, @event.Type);
    }

    [Fact]
    public void ObligationLinkedToInstitution_CanBeCreated_WithValidData()
    {
        // Arrange
        var obligationId = Guid.NewGuid();
        var institutionId = Guid.NewGuid();

        // Act
        var @event = new ObligationLinkedToInstitution(
            ObligationId: obligationId,
            InstitutionId: institutionId,
            ProductCode: "MORTGAGE_STANDARD",
            ContractReference: "MTG-2025-001234",
            EffectiveDate: new DateOnly(2025, 1, 1)
        );

        // Assert
        Assert.Equal(obligationId, @event.ObligationId);
        Assert.Equal(institutionId, @event.InstitutionId);
        Assert.Equal("MORTGAGE_STANDARD", @event.ProductCode);
        Assert.Equal("MTG-2025-001234", @event.ContractReference);
    }

    [Fact]
    public void ObligationLinkedToInstitution_AllowsNullOptionalFields()
    {
        // Arrange & Act
        var @event = new ObligationLinkedToInstitution(
            ObligationId: Guid.NewGuid(),
            InstitutionId: Guid.NewGuid(),
            ProductCode: null,
            ContractReference: null,
            EffectiveDate: new DateOnly(2025, 1, 1)
        );

        // Assert
        Assert.Null(@event.ProductCode);
        Assert.Null(@event.ContractReference);
    }
}
