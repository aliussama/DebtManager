using DebtManager.Domain.Events;
using DebtManager.Domain.Installments;

namespace DebtManager.Domain.Allocation;

public interface IPaymentAllocator
{
    AllocationResult Allocate(
        PaymentMade payment,
        IReadOnlyList<ExpectedInstallment> expectedInstallments,
        IReadOnlyList<PaymentAllocated> existingAllocations,
        DateOnly asOfDate
    );
}
