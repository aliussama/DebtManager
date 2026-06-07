namespace DebtManager.Domain.Rules;

public static class RuleEffectTypes
{
    // Charges (additions to amount owed)
    public const string Charge = "charge";                 // penalty/interest/fee/tax
    public const string InterestAccrual = "interest_accrual";

    // Allocation / payment guidance (future)
    public const string AllocationHint = "allocation_hint";

    // Warnings (decision support)
    public const string Warning = "warning";
}

public static class RuleEffectFields
{
    // Common
    public const string Label = "label";
    public const string RuleKey = "ruleKey";

    // Charge fields
    public const string Amount = "amount";          // decimal
    public const string ChargeType = "chargeType";  // penalty/interest/fee/tax/other

    // Interest fields
    public const string Rate = "rate";              // decimal (e.g. 0.18)
    public const string Compounding = "compounding"; // "daily"|"monthly"|"simple"
    public const string Basis = "basis";            // "actual365"|"30e360" etc.
}
