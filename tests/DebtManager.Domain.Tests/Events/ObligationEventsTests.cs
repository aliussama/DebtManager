using DebtManager.Domain.Events;
using Xunit;

namespace DebtManager.Domain.Tests.Events;

public class ObligationEventsTests
{
    [Fact]
    public void ObligationClosed_CanBeCreated_WithValidData()
    {
        // Arrange & Act
        var @event = new ObligationClosed(
            ObligationId: Guid.NewGuid(),
            ClosureDate: new DateOnly(2025, 6, 15),
            ClosureType: ObligationClosureType.PaidInFull,
            FinalBalance: new ValueObjects.Money(0m, ValueObjects.Currency.EGP),
            Reason: "All installments paid",
            Notes: null
        );

        // Assert
        Assert.Equal(ObligationClosureType.PaidInFull, @event.ClosureType);
        Assert.Equal(0m, @event.FinalBalance.Amount);
    }

    [Fact]
    public void ObligationClosed_EarlySettlement_TracksReason()
    {
        // Arrange & Act
        var @event = new ObligationClosed(
            ObligationId: Guid.NewGuid(),
            ClosureDate: new DateOnly(2025, 6, 15),
            ClosureType: ObligationClosureType.EarlySettlement,
            FinalBalance: new ValueObjects.Money(0m, ValueObjects.Currency.EGP),
            Reason: "Lump sum payment received",
            Notes: "Discount of 5% applied"
        );

        // Assert
        Assert.Equal(ObligationClosureType.EarlySettlement, @event.ClosureType);
        Assert.Equal("Lump sum payment received", @event.Reason);
        Assert.Equal("Discount of 5% applied", @event.Notes);
    }

    [Fact]
    public void ChargeWaived_CanBeCreated_WithValidData()
    {
        // Arrange & Act
        var @event = new ChargeWaived(
            WaiverId: Guid.NewGuid(),
            ObligationId: Guid.NewGuid(),
            InstallmentKey: Guid.NewGuid(),
            ChargeType: "penalty",
            WaivedAmount: new ValueObjects.Money(150m, ValueObjects.Currency.EGP),
            Reason: "Customer goodwill gesture",
            ApprovedBy: Guid.NewGuid(),
            EffectiveDate: new DateOnly(2025, 6, 15)
        );

        // Assert
        Assert.Equal(150m, @event.WaivedAmount.Amount);
        Assert.Equal("penalty", @event.ChargeType);
    }
}
