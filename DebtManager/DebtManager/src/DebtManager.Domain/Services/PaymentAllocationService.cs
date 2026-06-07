using DebtManager.Domain.Allocation;
using DebtManager.Domain.Events;
using DebtManager.Domain.Installments;

namespace DebtManager.Domain.Services;

public sealed class PaymentAllocationService
{
    private readonly IPaymentAllocator _allocator;

    public PaymentAllocationService(IPaymentAllocator allocator)
    {
        _allocator = allocator;
    }

    public IReadOnlyList<PaymentAllocated> AllocatePayment(
        Guid paymentEventId,
        PaymentMade payment,
        IReadOnlyList<ExpectedInstallment> expected,
        IReadOnlyList<PaymentAllocated> existingAllocations,
        DateOnly asOfDate)
    {
        var result = _allocator.Allocate(payment, expected, existingAllocations, asOfDate);

        // Convert allocation lines to allocation events (auditable truth)
        return result.Lines
            .Select(line => new PaymentAllocated(
                paymentEventId,
                payment.ObligationId,
                line.InstallmentKey,
                line.AppliedAmount,
                payment.EffectiveDate))
            .ToList()
            .AsReadOnly();
    }
}
