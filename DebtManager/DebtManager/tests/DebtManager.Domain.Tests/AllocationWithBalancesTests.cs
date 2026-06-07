using DebtManager.Domain.Allocation;
using DebtManager.Domain.Events;
using DebtManager.Domain.Installments;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Tests;

public class AllocationWithBalancesTests
{
    [Fact]
    public void Allocator_RespectsExistingAllocations()
    {
        var egp = Currency.EGP;
        var obligationId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();

        var inst1 = new ExpectedInstallment(InstallmentKey.New(), obligationId, new DateOnly(2026, 9, 15), new Money(10000, egp), scheduleId, new[] { "tuition" });
        var inst2 = new ExpectedInstallment(InstallmentKey.New(), obligationId, new DateOnly(2026, 11, 30), new Money(10000, egp), scheduleId, new[] { "tuition" });

        var expected = new[] { inst1, inst2 };

        // Payment 1 already allocated 6000 to inst1
        var existingAlloc = new[]
        {
            new PaymentAllocated(Guid.NewGuid(), obligationId, inst1.InstallmentKey.Value, new Money(6000, egp), new DateOnly(2026, 9, 20))
        };

        // New payment of 7000 should finish inst1 (remaining 4000), then apply 3000 to inst2
        var payment2 = new PaymentMade(obligationId, new Money(7000, egp), new DateOnly(2026, 10, 1), "Second payment");

        var allocator = new OldestDueFirstAllocator();

        var result = allocator.Allocate(payment2, expected, existingAlloc, new DateOnly(2026, 10, 1));

        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(4000, result.Lines[0].AppliedAmount.Amount);
        Assert.Equal(inst1.InstallmentKey.Value, result.Lines[0].InstallmentKey);

        Assert.Equal(3000, result.Lines[1].AppliedAmount.Amount);
        Assert.Equal(inst2.InstallmentKey.Value, result.Lines[1].InstallmentKey);

        Assert.Equal(0, result.UnallocatedRemainder.Amount);
    }
}
