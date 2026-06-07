using DebtManager.Domain.Events;
using DebtManager.Domain.Installments;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Tests;

public class ProjectionTests
{
    [Fact]
    public void Replay_BuildsCorrectTotals()
    {
        var egp = Currency.EGP;

        var obligationId = Guid.NewGuid();

        var events = new IDomainEvent[]
        {
            new ObligationCreated(
                obligationId,
                "Tuition",
                "Education",
                new Money(10000, egp),
                new DateOnly(2024, 9, 15),
                "EGP"),

            new IncomeReceived(
                new Money(20000, egp),
                new DateOnly(2024, 9, 10),
                "Salary"),

            new PaymentMade(
                obligationId,
                new Money(5000, egp),
                new DateOnly(2024, 9, 20),
                "First installment")
        };

        var projector = new FinancialProjector();

        var state = projector.Replay(
            events,
            Array.Empty<ExpectedInstallment>(),
            new ProjectionContext(new DateOnly(2024, 12, 31), egp));

        Assert.Equal(20000, state.TotalIncome.Amount);
        Assert.Equal(5000, state.TotalPayments.Amount);

        state.Obligations[obligationId]
             .TotalPaid.Amount
             .Equals(5000);
    }
}
