using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Projections.Charges;
using DebtManager.Domain.Projections.Installments;
using DebtManager.Domain.ValueObjects;
using DebtManager.Reporting.Models;

namespace DebtManager.Reporting.Services;

/// <summary>
/// Service to generate portfolio dashboard from financial state.
/// </summary>
public sealed class DashboardGenerator
{
    /// <summary>
    /// Generate a portfolio dashboard from obligation states.
    /// </summary>
    public PortfolioDashboard Generate(
        IReadOnlyList<ObligationSnapshot> obligations,
        DateOnly asOfDate,
        Currency currency)
    {
        var summaries = obligations
            .Select(o => GenerateObligationSummary(o, asOfDate))
            .ToList();

        var activeObligations = summaries.Where(s => !s.IsClosed).ToList();

        // Calculate totals
        var totalPrincipal = SumMoney(summaries.Select(s => s.Principal), currency);
        var totalOutstanding = SumMoney(activeObligations.Select(s => s.OutstandingBalance), currency);
        var totalPaid = SumMoney(summaries.Select(s => s.TotalPaid), currency);
        var totalInterest = SumMoney(summaries.Select(s => s.AccruedInterest), currency);
        var totalPenalties = SumMoney(summaries.Select(s => s.AccruedPenalties), currency);

        // Health counts
        var healthyCounts = activeObligations.Count(s => s.HealthStatus == ObligationHealthStatus.Healthy);
        var atRiskCounts = activeObligations.Count(s => s.HealthStatus == ObligationHealthStatus.AtRisk);
        var delinquentCounts = activeObligations.Count(s => s.HealthStatus == ObligationHealthStatus.Delinquent);
        var criticalCounts = activeObligations.Count(s => s.HealthStatus == ObligationHealthStatus.Critical);

        // Upcoming payments
        var next7Days = asOfDate.AddDays(7);
        var next30Days = asOfDate.AddDays(30);

        var upcoming7 = obligations
            .SelectMany(o => o.Installments)
            .Where(i => i.DueDate > asOfDate && i.DueDate <= next7Days && i.Status != InstallmentStatus.Paid)
            .ToList();

        var upcoming30 = obligations
            .SelectMany(o => o.Installments)
            .Where(i => i.DueDate > asOfDate && i.DueDate <= next30Days && i.Status != InstallmentStatus.Paid)
            .ToList();

        var totalDue7 = SumMoney(upcoming7.Select(i => i.ExpectedAmount), currency);
        var totalDue30 = SumMoney(upcoming30.Select(i => i.ExpectedAmount), currency);

        // Overdue
        var overdue = obligations
            .SelectMany(o => o.Installments)
            .Where(i => i.Status == InstallmentStatus.Overdue)
            .ToList();

        var totalOverdue = SumMoney(overdue.Select(i => i.ExpectedAmount.Subtract(i.PaidAmount)), currency);

        return new PortfolioDashboard(
            AsOfDate: asOfDate,
            CurrencyCode: currency.Code,
            TotalPrincipal: totalPrincipal,
            TotalOutstanding: totalOutstanding,
            TotalPaid: totalPaid,
            TotalInterestAccrued: totalInterest,
            TotalPenaltiesAccrued: totalPenalties,
            TotalWaivedCharges: Money.Zero(currency), // TODO: Calculate from waiver events
            TotalObligations: summaries.Count,
            ActiveObligations: activeObligations.Count,
            ClosedObligations: summaries.Count(s => s.IsClosed),
            HealthyObligations: healthyCounts,
            AtRiskObligations: atRiskCounts,
            DelinquentObligations: delinquentCounts,
            CriticalObligations: criticalCounts,
            UpcomingPaymentsNext7Days: upcoming7.Count,
            UpcomingPaymentsNext30Days: upcoming30.Count,
            TotalDueNext7Days: totalDue7,
            TotalDueNext30Days: totalDue30,
            OverdueInstallmentsCount: overdue.Count,
            TotalOverdueAmount: totalOverdue,
            Obligations: summaries.AsReadOnly()
        );
    }

    private ObligationSummary GenerateObligationSummary(ObligationSnapshot snapshot, DateOnly asOfDate)
    {
        var overdueInstallments = snapshot.Installments
            .Where(i => i.Status == InstallmentStatus.Overdue)
            .ToList();

        var paidInstallments = snapshot.Installments
            .Count(i => i.Status == InstallmentStatus.Paid);

        var nextDue = snapshot.Installments
            .Where(i => i.DueDate >= asOfDate && i.Status != InstallmentStatus.Paid)
            .OrderBy(i => i.DueDate)
            .FirstOrDefault();

        var daysUntilNextDue = nextDue != null
            ? nextDue.DueDate.DayNumber - asOfDate.DayNumber
            : 0;

        var maxDaysOverdue = overdueInstallments.Any()
            ? overdueInstallments.Max(i => asOfDate.DayNumber - i.DueDate.DayNumber)
            : 0;

        var healthStatus = DetermineHealthStatus(maxDaysOverdue, snapshot.IsClosed);

        var accruedInterest = SumMoney(
            snapshot.Charges.Where(c => c.Type == ChargeType.Interest).Select(c => c.Amount),
            snapshot.Currency
        );

        var accruedPenalties = SumMoney(
            snapshot.Charges.Where(c => c.Type == ChargeType.Penalty).Select(c => c.Amount),
            snapshot.Currency
        );

        return new ObligationSummary(
            ObligationId: snapshot.ObligationId,
            Name: snapshot.Name,
            ObligationType: snapshot.ObligationType,
            Principal: snapshot.Principal,
            TotalPaid: snapshot.TotalPaid,
            OutstandingBalance: snapshot.OutstandingBalance,
            AccruedInterest: accruedInterest,
            AccruedPenalties: accruedPenalties,
            TotalInstallments: snapshot.Installments.Count,
            PaidInstallments: paidInstallments,
            OverdueInstallments: overdueInstallments.Count,
            NextDueDate: nextDue?.DueDate,
            NextPaymentAmount: nextDue?.ExpectedAmount,
            DaysUntilNextDue: daysUntilNextDue,
            HealthStatus: healthStatus,
            IsClosed: snapshot.IsClosed,
            ClosureDate: snapshot.ClosureDate
        );
    }

    private static ObligationHealthStatus DetermineHealthStatus(int maxDaysOverdue, bool isClosed)
    {
        if (isClosed) return ObligationHealthStatus.Closed;
        if (maxDaysOverdue == 0) return ObligationHealthStatus.Healthy;
        if (maxDaysOverdue <= 30) return ObligationHealthStatus.AtRisk;
        if (maxDaysOverdue <= 90) return ObligationHealthStatus.Delinquent;
        return ObligationHealthStatus.Critical;
    }

    private static Money SumMoney(IEnumerable<Money> amounts, Currency currency)
    {
        return amounts.Aggregate(Money.Zero(currency), (acc, m) => acc.Add(m));
    }
}

/// <summary>
/// Snapshot of an obligation for reporting.
/// </summary>
public sealed record ObligationSnapshot(
    Guid ObligationId,
    string Name,
    string ObligationType,
    Currency Currency,
    Money Principal,
    Money TotalPaid,
    Money OutstandingBalance,
    bool IsClosed,
    DateOnly? ClosureDate,
    IReadOnlyList<InstallmentSnapshot> Installments,
    IReadOnlyList<ComputedCharge> Charges
);

/// <summary>
/// Snapshot of an installment for reporting.
/// </summary>
public sealed record InstallmentSnapshot(
    string InstallmentKey,
    DateOnly DueDate,
    Money ExpectedAmount,
    Money PaidAmount,
    InstallmentStatus Status
);
