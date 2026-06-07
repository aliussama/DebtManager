using DebtManager.Domain.Projections;
using DebtManager.Domain.Projections.Charges;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Services.Allocation;

public sealed class AllocationPolicyV1
{
    public sealed record Result(
        IReadOnlyList<(Guid ChargeId, Money Amount)> ChargeAllocations,
        Money RemainingForInstallments
    );

    public Result AllocateChargesFirst(FinancialState state, Money payment)
    {
        var remaining = payment;
        var chargeAllocs = new List<(Guid, Money)>();

        foreach (var c in state.Charges
                     .Where(x => x.Amount.Amount > 0m)
                     .OrderBy(x => Priority(x.Type))
                     .ThenBy(x => x.EffectiveDate))
        {
            if (remaining.Amount <= 0m) break;

            var take = Math.Min(remaining.Amount, c.Amount.Amount);
            if (take <= 0m) continue;

            var m = new Money(take, remaining.Currency);
            chargeAllocs.Add((c.ChargeId, m));
            remaining = remaining.Subtract(m);
        }

        return new Result(chargeAllocs, remaining);
    }

    private static int Priority(ChargeType t) => t switch
    {
        ChargeType.Tax => 0,
        ChargeType.Fee => 1,
        ChargeType.Penalty => 2,
        ChargeType.Interest => 3,
        _ => 9
    };
}
