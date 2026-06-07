using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Installments;

public sealed record ExpectedInstallment(
    InstallmentKey InstallmentKey,
    Guid ObligationId,
    DateOnly DueDate,
    Money Amount,
    Guid ScheduleId,
    IReadOnlyList<string> Tags
);

