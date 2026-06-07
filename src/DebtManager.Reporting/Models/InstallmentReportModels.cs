using DebtManager.Domain.ValueObjects;

namespace DebtManager.Reporting.Models;

/// <summary>
/// Detailed installment report entry.
/// </summary>
public sealed record InstallmentReportEntry(
    string InstallmentKey,
    Guid ObligationId,
    string ObligationName,
    DateOnly DueDate,
    Money ExpectedAmount,
    Money PaidAmount,
    Money OutstandingAmount,
    Money InterestCharge,
    Money PenaltyCharge,
    InstallmentPaymentStatus Status,
    int DaysOverdue,
    DateOnly? LastPaymentDate
);

/// <summary>
/// Payment status for installment.
/// </summary>
public enum InstallmentPaymentStatus
{
    NotDue,
    Due,
    PartiallyPaid,
    Paid,
    Overdue,
    Waived
}

/// <summary>
/// Payment history entry.
/// </summary>
public sealed record PaymentHistoryEntry(
    Guid PaymentId,
    DateOnly PaymentDate,
    Money Amount,
    string PaymentMethod,
    IReadOnlyList<PaymentAllocationEntry> Allocations,
    string? Reference,
    string? Notes
);

/// <summary>
/// How a payment was allocated.
/// </summary>
public sealed record PaymentAllocationEntry(
    Guid ObligationId,
    string ObligationName,
    string? InstallmentKey,
    string ChargeType,
    Money AllocatedAmount
);

/// <summary>
/// Charge breakdown report.
/// </summary>
public sealed record ChargeBreakdownReport(
    Guid ObligationId,
    string ObligationName,
    DateOnly AsOfDate,
    Money TotalPrincipal,
    Money PrincipalPaid,
    Money PrincipalOutstanding,
    Money TotalInterestCharged,
    Money InterestPaid,
    Money InterestOutstanding,
    Money TotalPenaltiesCharged,
    Money PenaltiesPaid,
    Money PenaltiesOutstanding,
    Money TotalFeesCharged,
    Money FeesPaid,
    Money FeesOutstanding,
    Money TotalWaived,
    Money GrandTotalOutstanding,
    IReadOnlyList<ChargeDetailEntry> ChargeDetails
);

/// <summary>
/// Individual charge detail.
/// </summary>
public sealed record ChargeDetailEntry(
    Guid ChargeId,
    DateOnly EffectiveDate,
    string ChargeType,
    string Label,
    Money Amount,
    Money Paid,
    Money Outstanding,
    string RuleKey,
    bool IsWaived
);
