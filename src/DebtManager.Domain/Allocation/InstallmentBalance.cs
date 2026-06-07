using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Allocation;

public sealed record InstallmentBalance(
    Guid ObligationId,
    Guid InstallmentKey,
    DateOnly DueDate,
    Money Expected,
    Money Paid
)
{
    public Money Outstanding => Expected.Subtract(Paid);
}
