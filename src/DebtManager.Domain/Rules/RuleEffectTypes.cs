namespace DebtManager.Domain.Rules;

public static class RuleEffectTypes
{
    // Charges (additions to amount owed)
    public const string Charge = "charge";
    public const string AddCharge = "add_charge";
    public const string InterestAccrual = "interest_accrual";
    public const string AccrueInterest = "accrue_interest";
    public const string ApplyPenalty = "apply_penalty";
    public const string ApplyFee = "apply_fee";
    public const string ApplyTax = "apply_tax";

    // Grace and scheduling
    public const string ApplyGrace = "apply_grace";
    public const string ShiftDueDate = "shift_due_date";
    public const string ExtendTerm = "extend_term";

    // Payment requirements
    public const string RequireMinimumPayment = "require_min_payment";
    public const string RequireFullPayment = "require_full_payment";

    // Discounts and waivers
    public const string ApplyDiscount = "apply_discount";
    public const string WaiveCharge = "waive_charge";

    // Allocation / payment guidance
    public const string AllocationHint = "allocation_hint";

    // Warnings (decision support)
    public const string Warning = "warning";

    // Compound effects
    public const string TriggerEscalation = "trigger_escalation";
    public const string ActivateRule = "activate_rule";
}

public static class RuleEffectFields
{
    // Common
    public const string Label = "label";
    public const string RuleKey = "ruleKey";
    public const string Amount = "amount";
    public const string ChargeType = "chargeType";
    public const string Currency = "currency";

    // Interest fields
    public const string Rate = "rate";
    public const string Compounding = "compounding";
    public const string Basis = "basis";
    public const string DayCountBasis = "basis";
    public const string PeriodStart = "periodStart";
    public const string PeriodEnd = "periodEnd";

    // Grace period specific
    public const string GraceDays = "graceDays";
    public const string GraceType = "graceType";
    public const string AppliesToPenalties = "appliesToPenalties";
    public const string AppliesToInterest = "appliesToInterest";

    // Penalty specific
    public const string PenaltyType = "penaltyType";
    public const string EscalationDays = "escalationDays";
    public const string MaxPenalty = "maxPenalty";

    // Scheduling specific
    public const string ShiftDays = "shiftDays";
    public const string NewDueDate = "newDueDate";
}
