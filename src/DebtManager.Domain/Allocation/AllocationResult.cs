using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Allocation;

public sealed record AllocationLine(
    Guid ObligationId,
    Guid InstallmentKey,
    DateOnly DueDate,
    Money AppliedAmount
);

public sealed record AllocationResult(
    Money PaymentAmount,
    Money UnallocatedRemainder,
    IReadOnlyList<AllocationLine> Lines
);
