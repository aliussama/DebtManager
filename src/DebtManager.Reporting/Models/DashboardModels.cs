using DebtManager.Domain.ValueObjects;

namespace DebtManager.Reporting.Models;

/// <summary>
/// Summary of a single obligation for dashboard display.
/// </summary>
public sealed record ObligationSummary(
    Guid ObligationId,
    string Name,
    string ObligationType,
    Money Principal,
    Money TotalPaid,
    Money OutstandingBalance,
    Money AccruedInterest,
    Money AccruedPenalties,
    int TotalInstallments,
    int PaidInstallments,
    int OverdueInstallments,
    DateOnly? NextDueDate,
    Money? NextPaymentAmount,
    int DaysUntilNextDue,
    ObligationHealthStatus HealthStatus,
    bool IsClosed,
    DateOnly? ClosureDate
);

/// <summary>
/// Health status of an obligation.
/// </summary>
public enum ObligationHealthStatus
{
    /// <summary>All payments on time, no issues.</summary>
    Healthy,
    /// <summary>1-30 days overdue.</summary>
    AtRisk,
    /// <summary>31-90 days overdue.</summary>
    Delinquent,
    /// <summary>90+ days overdue.</summary>
    Critical,
    /// <summary>Obligation is closed.</summary>
    Closed
}

/// <summary>
/// Portfolio-level dashboard summary.
/// </summary>
public sealed record PortfolioDashboard(
    DateOnly AsOfDate,
    string CurrencyCode,
    
    // Totals
    Money TotalPrincipal,
    Money TotalOutstanding,
    Money TotalPaid,
    Money TotalInterestAccrued,
    Money TotalPenaltiesAccrued,
    Money TotalWaivedCharges,
    
    // Counts
    int TotalObligations,
    int ActiveObligations,
    int ClosedObligations,
    int HealthyObligations,
    int AtRiskObligations,
    int DelinquentObligations,
    int CriticalObligations,
    
    // Upcoming
    int UpcomingPaymentsNext7Days,
    int UpcomingPaymentsNext30Days,
    Money TotalDueNext7Days,
    Money TotalDueNext30Days,
    
    // Overdue
    int OverdueInstallmentsCount,
    Money TotalOverdueAmount,
    
    // Obligations list
    IReadOnlyList<ObligationSummary> Obligations
);

/// <summary>
/// Monthly payment summary for trend analysis.
/// </summary>
public sealed record MonthlyPaymentSummary(
    int Year,
    int Month,
    Money TotalDue,
    Money TotalPaid,
    Money TotalOverdue,
    int InstallmentsDue,
    int InstallmentsPaid,
    int InstallmentsOverdue
);

/// <summary>
/// Payment projection for cash flow planning.
/// </summary>
public sealed record PaymentProjection(
    DateOnly DueDate,
    Guid ObligationId,
    string ObligationName,
    string InstallmentKey,
    Money Amount,
    bool IsPaid,
    bool IsOverdue,
    int DaysOverdue
);

/// <summary>
/// Debt payoff projection showing when each obligation will be paid off.
/// </summary>
public sealed record DebtPayoffProjection(
    Guid ObligationId,
    string Name,
    Money CurrentBalance,
    DateOnly ProjectedPayoffDate,
    int RemainingPayments,
    Money TotalRemainingPayments,
    Money TotalInterestRemaining
);
