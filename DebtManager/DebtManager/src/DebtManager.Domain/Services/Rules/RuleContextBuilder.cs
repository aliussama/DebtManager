using DebtManager.Domain.Projections.Installments;
using DebtManager.Domain.Rules;

namespace DebtManager.Domain.Services.Rules;

public sealed class RuleContextBuilder
{
    public RuleEvaluationContext BuildForInstallment(
        InstallmentState installment,
        DateOnly evaluationDate,
        string currencyCode)
    {
        // Facts must be stable keys; UI can show them; rules can depend on them.
        var facts = new Dictionary<string, object>
        {
            ["installment.due_date"] = installment.DueDate.ToString("yyyy-MM-dd"),
            ["installment.status"] = installment.Status.ToString(),
            ["installment.days_overdue"] = installment.DaysOverdue,
            ["installment.is_fully_paid"] = installment.IsFullyPaid,

            ["money.expected"] = installment.Expected.Amount,
            ["money.paid"] = installment.Paid.Amount,
            ["money.outstanding"] = installment.Outstanding.Amount,
        };

        return new RuleEvaluationContext(
            EvaluationDate: evaluationDate,
            ObligationId: installment.ObligationId,
            InstallmentKey: installment.InstallmentKey,
            CurrencyCode: currencyCode,
            Facts: facts
        );
    }
}
