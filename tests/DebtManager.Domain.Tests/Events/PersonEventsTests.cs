using DebtManager.Domain.Events;
using Xunit;

namespace DebtManager.Domain.Tests.Events;

public class PersonEventsTests
{
    [Fact]
    public void PersonCreated_CanBeCreated_WithValidData()
    {
        // Arrange & Act
        var @event = new PersonCreated(
            PersonId: Guid.NewGuid(),
            FullName: "Ahmed Mohamed",
            PrimaryRole: PersonRole.Self,
            Contact: new ContactInfo(
                Email: "ahmed@example.com",
                Phone: "+20123456789",
                Address: "Cairo, Egypt"
            ),
            EffectiveDate: new DateOnly(2025, 1, 1)
        );

        // Assert
        Assert.Equal("Ahmed Mohamed", @event.FullName);
        Assert.Equal(PersonRole.Self, @event.PrimaryRole);
        Assert.NotNull(@event.Contact);
        Assert.Equal("ahmed@example.com", @event.Contact.Email);
    }

    [Fact]
    public void PersonLinkedToObligation_CanBeCreated_WithValidData()
    {
        // Arrange
        var personId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        // Act
        var @event = new PersonLinkedToObligation(
            PersonId: personId,
            ObligationId: obligationId,
            Role: ObligationRole.PrimaryDebtor,
            EffectiveFrom: new DateOnly(2025, 1, 1)
        );

        // Assert
        Assert.Equal(personId, @event.PersonId);
        Assert.Equal(obligationId, @event.ObligationId);
        Assert.Equal(ObligationRole.PrimaryDebtor, @event.Role);
    }

    [Theory]
    [InlineData(ObligationRole.PrimaryDebtor)]
    [InlineData(ObligationRole.CoDebtor)]
    [InlineData(ObligationRole.Guarantor)]
    [InlineData(ObligationRole.Creditor)]
    public void PersonLinkedToObligation_SupportsAllRoles(ObligationRole role)
    {
        // Arrange & Act
        var @event = new PersonLinkedToObligation(
            PersonId: Guid.NewGuid(),
            ObligationId: Guid.NewGuid(),
            Role: role,
            EffectiveFrom: new DateOnly(2025, 1, 1)
        );

        // Assert
        Assert.Equal(role, @event.Role);
    }
}
