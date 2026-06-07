using DebtManager.Domain.Projections.Charges;
using DebtManager.Domain.ValueObjects;
using DebtManager.Reporting.Models;

namespace DebtManager.Reporting.Services;

/// <summary>
/// Service to generate charge breakdown reports.
/// </summary>
public sealed class ChargeReportGenerator
{
    /// <summary>
    /// Generate a detailed charge breakdown for an obligation.
    /// </summary>
    public ChargeBreakdownReport Generate(
        ObligationSnapshot obligation,
        DateOnly asOfDate)
    {
        var currency = obligation.Currency;

        // Group charges by type
        var interestCharges = obligation.Charges.Where(c => c.Type == ChargeType.Interest).ToList();
        var penaltyCharges = obligation.Charges.Where(c => c.Type == ChargeType.Penalty).ToList();
        var feeCharges = obligation.Charges.Where(c => c.Type == ChargeType.Fee).ToList();
        var taxCharges = obligation.Charges.Where(c => c.Type == ChargeType.Tax).ToList();

        // Calculate principal amounts
        var principalPaid = obligation.TotalPaid; // Simplified - actual would track allocation
        var principalOutstanding = obligation.OutstandingBalance;

        // Calculate charge totals
        var totalInterest = SumMoney(interestCharges.Select(c => c.Amount), currency);
        var totalPenalties = SumMoney(penaltyCharges.Select(c => c.Amount), currency);
        var totalFees = SumMoney(feeCharges.Select(c => c.Amount), currency);

        // TODO: Track actual paid amounts per charge
        var interestPaid = Money.Zero(currency);
        var penaltiesPaid = Money.Zero(currency);
        var feesPaid = Money.Zero(currency);

        var chargeDetails = obligation.Charges
            .Select(c => new ChargeDetailEntry(
                ChargeId: c.ChargeId,
                EffectiveDate: c.EffectiveDate,
                ChargeType: c.Type.ToString(),
                Label: c.Label,
                Amount: c.Amount,
                Paid: Money.Zero(currency), // TODO: Track actual payments
                Outstanding: c.Amount,
                RuleKey: c.RuleKey,
                IsWaived: false // TODO: Track waivers
            ))
            .OrderBy(c => c.EffectiveDate)
            .ToList();

        var grandTotal = principalOutstanding
            .Add(totalInterest)
            .Add(totalPenalties)
            .Add(totalFees);

        return new ChargeBreakdownReport(
            ObligationId: obligation.ObligationId,
            ObligationName: obligation.Name,
            AsOfDate: asOfDate,
            TotalPrincipal: obligation.Principal,
            PrincipalPaid: principalPaid,
            PrincipalOutstanding: principalOutstanding,
            TotalInterestCharged: totalInterest,
            InterestPaid: interestPaid,
            InterestOutstanding: totalInterest,
            TotalPenaltiesCharged: totalPenalties,
            PenaltiesPaid: penaltiesPaid,
            PenaltiesOutstanding: totalPenalties,
            TotalFeesCharged: totalFees,
            FeesPaid: feesPaid,
            FeesOutstanding: totalFees,
            TotalWaived: Money.Zero(currency),
            GrandTotalOutstanding: grandTotal,
            ChargeDetails: chargeDetails.AsReadOnly()
        );
    }

    /// <summary>
    /// Generate installment report entries.
    /// </summary>
    public IReadOnlyList<InstallmentReportEntry> GenerateInstallmentReport(
        ObligationSnapshot obligation,
        DateOnly asOfDate)
    {
        var entries = new List<InstallmentReportEntry>();
        var currency = obligation.Currency;

        foreach (var installment in obligation.Installments.OrderBy(i => i.DueDate))
        {
            var outstandingAmount = installment.ExpectedAmount.Subtract(installment.PaidAmount);
            var daysOverdue = installment.Status == Domain.Projections.Installments.InstallmentStatus.Overdue
                ? asOfDate.DayNumber - installment.DueDate.DayNumber
                : 0;

            var paymentStatus = MapStatus(installment.Status, installment.DueDate, asOfDate);

            // Find charges associated with this installment
            var installmentCharges = obligation.Charges
                .Where(c => c.InstallmentKey?.ToString() == installment.InstallmentKey)
                .ToList();

            var interestCharge = SumMoney(
                installmentCharges.Where(c => c.Type == ChargeType.Interest).Select(c => c.Amount),
                currency
            );

            var penaltyCharge = SumMoney(
                installmentCharges.Where(c => c.Type == ChargeType.Penalty).Select(c => c.Amount),
                currency
            );

            entries.Add(new InstallmentReportEntry(
                InstallmentKey: installment.InstallmentKey,
                ObligationId: obligation.ObligationId,
                ObligationName: obligation.Name,
                DueDate: installment.DueDate,
                ExpectedAmount: installment.ExpectedAmount,
                PaidAmount: installment.PaidAmount,
                OutstandingAmount: outstandingAmount,
                InterestCharge: interestCharge,
                PenaltyCharge: penaltyCharge,
                Status: paymentStatus,
                DaysOverdue: daysOverdue,
                LastPaymentDate: null // TODO: Track from payment events
            ));
        }

        return entries.AsReadOnly();
    }

    private static InstallmentPaymentStatus MapStatus(
        Domain.Projections.Installments.InstallmentStatus status,
        DateOnly dueDate,
        DateOnly asOfDate)
    {
        return status switch
        {
            Domain.Projections.Installments.InstallmentStatus.Paid => InstallmentPaymentStatus.Paid,
            Domain.Projections.Installments.InstallmentStatus.Overdue => InstallmentPaymentStatus.Overdue,
            Domain.Projections.Installments.InstallmentStatus.PartiallyPaid => InstallmentPaymentStatus.PartiallyPaid,
            _ => dueDate > asOfDate ? InstallmentPaymentStatus.NotDue : InstallmentPaymentStatus.Due
        };
    }

    private static Money SumMoney(IEnumerable<Money> amounts, Currency currency)
    {
        return amounts.Aggregate(Money.Zero(currency), (acc, m) => acc.Add(m));
    }
}
