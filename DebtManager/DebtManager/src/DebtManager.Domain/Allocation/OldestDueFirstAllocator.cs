using DebtManager.Domain.Events;
using DebtManager.Domain.Installments;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Allocation;

public sealed class OldestDueFirstAllocator : IPaymentAllocator
{
    private readonly InstallmentBalanceBuilder _balanceBuilder = new();

    public AllocationResult Allocate(
        PaymentMade payment,
        IReadOnlyList<ExpectedInstallment> expectedInstallments,
        IReadOnlyList<PaymentAllocated> existingAllocations,
        DateOnly asOfDate)
    {
        var candidates = expectedInstallments
            .Where(i => i.ObligationId == payment.ObligationId)
            .ToList();

        var balances = _balanceBuilder.Build(
            expected: candidates,
            allocations: existingAllocations.ToList(),
            reversedAllocations: Array.Empty<DebtManager.Domain.Events.PaymentAllocationReversed>(),
            asOfDate: asOfDate);

        var remaining = payment.Amount;
        var lines = new List<AllocationLine>();

        foreach (var bal in balances.OrderBy(b => b.DueDate))
        {
            if (remaining.Amount <= 0m) break;

            var outstanding = bal.Outstanding;
            if (outstanding.Amount <= 0m) continue;

            var applied = Min(remaining, outstanding);

            lines.Add(new AllocationLine(
                bal.ObligationId,
                bal.InstallmentKey,
                bal.DueDate,
                applied));

            remaining = remaining.Subtract(applied);
        }

        return new AllocationResult(payment.Amount, remaining, lines.AsReadOnly());
    }

    private static Money Min(Money a, Money b)
    {
        if (a.Currency.Code != b.Currency.Code)
            throw new InvalidOperationException("Currency mismatch in allocation.");

        return a.Amount <= b.Amount ? a : b;
    }
}
