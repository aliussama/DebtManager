using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections.Installments;

public sealed record InstallmentState(
    Guid ObligationId,
    Guid InstallmentKey,
    DateOnly DueDate,
    Money Expected,
    Money Paid,
    InstallmentStatus Status,
    int DaysOverdue,
    InstallmentRisk Risk
)
{
    public Money Outstanding => Expected.Subtract(Paid);
    public bool IsFullyPaid => Outstanding.Amount <= 0m;
}
