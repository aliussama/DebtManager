using DebtManager.Domain.Events;
using DebtManager.Domain.Installments;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Allocation;

public sealed class InstallmentBalanceBuilder
{
    public IReadOnlyList<InstallmentBalance> Build(
        IReadOnlyList<ExpectedInstallment> expected,
        IReadOnlyList<PaymentAllocated> allocations,
        IReadOnlyList<PaymentAllocationReversed> reversedAllocations,
        DateOnly asOfDate)
    {
        // Only consider allocations effective up to the given date
        var relevantAllocations = allocations
            .Where(a => a.EffectiveDate <= asOfDate)
            .ToList();

        var paidByInstallment = relevantAllocations
            .GroupBy(a => a.InstallmentKey)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var first = g.First();
                    var currency = first.Amount.Currency;
                    var sum = Money.Zero(currency);
                    foreach (var a in g)
                        sum = sum.Add(a.Amount);
                    return sum;
                });

        var result = new List<InstallmentBalance>(expected.Count);

        foreach (var inst in expected)
        {
            var paid = paidByInstallment.TryGetValue(inst.InstallmentKey.Value, out var p)
                ? p
                : Money.Zero(inst.Amount.Currency);

            // Guard: never allow paid > expected without explicit overpay rules later
            if (paid.Amount > inst.Amount.Amount)
                paid = inst.Amount;

            result.Add(new InstallmentBalance(
                inst.ObligationId,
                inst.InstallmentKey.Value,
                inst.DueDate,
                inst.Amount,
                paid
            ));
        }

        return result
            .OrderBy(b => b.DueDate)
            .ToList()
            .AsReadOnly();
    }
}
