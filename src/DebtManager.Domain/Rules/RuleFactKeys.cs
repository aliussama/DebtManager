namespace DebtManager.Domain.Rules;

public static class RuleFactKeys
{
    public const string InstallmentDueDate = "installment_due_date";       // DateOnly or "yyyy-MM-dd"
    public const string DaysOverdue = "days_overdue";                       // int
    public const string OutstandingAmount = "outstanding_amount";           // decimal
    public const string OutstandingCurrency = "outstanding_currency";       // string
    public const string InstallmentExpectedAmount = "installment_expected"; // decimal (optional)
    public const string InstallmentPaidAmount = "installment_paid";         // decimal (optional)
}
