using DebtManager.Domain.ValueObjects;
using DebtManager.Domain.Projections.Installments;
using DebtManager.Domain.Projections.Charges;
using DebtManager.Domain.Audit;

namespace DebtManager.Domain.Projections;

public sealed class FinancialState
{
    public Money TotalIncome { get; private set; }
    public Money TotalPayments { get; private set; }
    public List<InstallmentState> Installments { get; } = new();
    public List<ComputedCharge> Charges { get; } = new();
    public List<AuditEntry> Audit { get; } = new();
    public Dictionary<Guid, Money> ChargePayments { get; } = new();
    public Money UnappliedPayments { get; private set; }
    public void RegisterIncome(Money amount)
    {
        TotalIncome = TotalIncome.Add(amount);
    }

    public void RegisterPayment(Money amount)
    {
        TotalPayments = TotalPayments.Add(amount);
    }
    public Dictionary<Guid, ObligationState> Obligations { get; }
    public FinancialState(Currency currency)
    {
        TotalIncome = Money.Zero(currency);
        TotalPayments = Money.Zero(currency);
        Obligations = new Dictionary<Guid, ObligationState>();
        UnappliedPayments = Money.Zero(currency);
    }
    public void RegisterObligation(ObligationState obligation)
    {
        Obligations[obligation.ObligationId] = obligation;
    }
    public void RegisterChargePayment(Guid chargeId, Money amount)
    {
        if (!ChargePayments.TryGetValue(chargeId, out var cur))
            ChargePayments[chargeId] = amount;
        else
            ChargePayments[chargeId] = cur.Add(amount);
    }
    public void RegisterUnapplied(Money amount) => UnappliedPayments = UnappliedPayments.Add(amount);
}
