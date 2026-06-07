using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

public sealed class ObligationState
{
    public Guid ObligationId { get; }
    public string Name { get; }
    public Money Principal { get; }

    public Money TotalPaid { get; private set; }

    public ObligationState(Guid obligationId, string name, Money principal)
    {
        ObligationId = obligationId;
        Name = name;
        Principal = principal;
        TotalPaid = Money.Zero(principal.Currency);
    }

    public void ApplyPayment(Money amount)
    {
        TotalPaid = TotalPaid.Add(amount);
    }
}
