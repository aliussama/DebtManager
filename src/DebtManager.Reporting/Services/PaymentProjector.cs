using DebtManager.Domain.ValueObjects;
using DebtManager.Reporting.Models;

namespace DebtManager.Reporting.Services;

/// <summary>
/// Service to project future payments for cash flow planning.
/// </summary>
public sealed class PaymentProjector
{
    /// <summary>
    /// Project payments for a date range.
    /// </summary>
    public IReadOnlyList<PaymentProjection> ProjectPayments(
        IReadOnlyList<ObligationSnapshot> obligations,
        DateOnly from,
        DateOnly to,
        DateOnly asOfDate)
    {
        var projections = new List<PaymentProjection>();

        foreach (var obligation in obligations.Where(o => !o.IsClosed))
        {
            foreach (var installment in obligation.Installments)
            {
                if (installment.DueDate < from || installment.DueDate > to)
                    continue;

                var isPaid = installment.Status == Domain.Projections.Installments.InstallmentStatus.Paid;
                var isOverdue = !isPaid && installment.DueDate < asOfDate;
                var daysOverdue = isOverdue
                    ? asOfDate.DayNumber - installment.DueDate.DayNumber
                    : 0;

                projections.Add(new PaymentProjection(
                    DueDate: installment.DueDate,
                    ObligationId: obligation.ObligationId,
                    ObligationName: obligation.Name,
                    InstallmentKey: installment.InstallmentKey,
                    Amount: installment.ExpectedAmount,
                    IsPaid: isPaid,
                    IsOverdue: isOverdue,
                    DaysOverdue: daysOverdue
                ));
            }
        }

        return projections
            .OrderBy(p => p.DueDate)
            .ThenBy(p => p.ObligationName)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Generate monthly payment summaries for trend analysis.
    /// </summary>
    public IReadOnlyList<MonthlyPaymentSummary> GenerateMonthlyTrend(
        IReadOnlyList<ObligationSnapshot> obligations,
        int year,
        Currency currency)
    {
        var summaries = new List<MonthlyPaymentSummary>();

        for (var month = 1; month <= 12; month++)
        {
            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var installmentsInMonth = obligations
                .SelectMany(o => o.Installments)
                .Where(i => i.DueDate >= monthStart && i.DueDate <= monthEnd)
                .ToList();

            var totalDue = SumMoney(installmentsInMonth.Select(i => i.ExpectedAmount), currency);
            var totalPaid = SumMoney(
                installmentsInMonth
                    .Where(i => i.Status == Domain.Projections.Installments.InstallmentStatus.Paid)
                    .Select(i => i.ExpectedAmount),
                currency
            );

            var overdueInMonth = installmentsInMonth
                .Where(i => i.Status == Domain.Projections.Installments.InstallmentStatus.Overdue)
                .ToList();

            var totalOverdue = SumMoney(
                overdueInMonth.Select(i => i.ExpectedAmount.Subtract(i.PaidAmount)),
                currency
            );

            summaries.Add(new MonthlyPaymentSummary(
                Year: year,
                Month: month,
                TotalDue: totalDue,
                TotalPaid: totalPaid,
                TotalOverdue: totalOverdue,
                InstallmentsDue: installmentsInMonth.Count,
                InstallmentsPaid: installmentsInMonth.Count(i =>
                    i.Status == Domain.Projections.Installments.InstallmentStatus.Paid),
                InstallmentsOverdue: overdueInMonth.Count
            ));
        }

        return summaries.AsReadOnly();
    }

    /// <summary>
    /// Generate debt payoff projections.
    /// </summary>
    public IReadOnlyList<DebtPayoffProjection> ProjectPayoffs(
        IReadOnlyList<ObligationSnapshot> obligations)
    {
        var projections = new List<DebtPayoffProjection>();

        foreach (var obligation in obligations.Where(o => !o.IsClosed))
        {
            var unpaidInstallments = obligation.Installments
                .Where(i => i.Status != Domain.Projections.Installments.InstallmentStatus.Paid)
                .OrderBy(i => i.DueDate)
                .ToList();

            if (!unpaidInstallments.Any())
            {
                projections.Add(new DebtPayoffProjection(
                    ObligationId: obligation.ObligationId,
                    Name: obligation.Name,
                    CurrentBalance: obligation.OutstandingBalance,
                    ProjectedPayoffDate: DateOnly.FromDateTime(DateTime.UtcNow),
                    RemainingPayments: 0,
                    TotalRemainingPayments: Money.Zero(obligation.Currency),
                    TotalInterestRemaining: Money.Zero(obligation.Currency)
                ));
                continue;
            }

            var lastPaymentDate = unpaidInstallments.Last().DueDate;
            var totalRemaining = SumMoney(unpaidInstallments.Select(i => i.ExpectedAmount), obligation.Currency);

            // Estimate remaining interest (simplified - actual would use amortization schedule)
            var interestCharges = obligation.Charges
                .Where(c => c.Type == Domain.Projections.Charges.ChargeType.Interest)
                .ToList();

            var avgMonthlyInterest = interestCharges.Any()
                ? interestCharges.Average(c => c.Amount.Amount)
                : 0m;

            var monthsRemaining = unpaidInstallments.Count;
            var estimatedInterest = new Money(avgMonthlyInterest * monthsRemaining, obligation.Currency);

            projections.Add(new DebtPayoffProjection(
                ObligationId: obligation.ObligationId,
                Name: obligation.Name,
                CurrentBalance: obligation.OutstandingBalance,
                ProjectedPayoffDate: lastPaymentDate,
                RemainingPayments: unpaidInstallments.Count,
                TotalRemainingPayments: totalRemaining,
                TotalInterestRemaining: estimatedInterest
            ));
        }

        return projections
            .OrderBy(p => p.ProjectedPayoffDate)
            .ToList()
            .AsReadOnly();
    }

    private static Money SumMoney(IEnumerable<Money> amounts, Currency currency)
    {
        return amounts.Aggregate(Money.Zero(currency), (acc, m) => acc.Add(m));
    }
}
