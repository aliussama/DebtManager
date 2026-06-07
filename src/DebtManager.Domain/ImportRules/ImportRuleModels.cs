using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DebtManager.Domain.ImportRules;

// ????????????????????????????????????????????
// Condition Operators
// ????????????????????????????????????????????

public enum StringMatchMode { Contains, Equals, StartsWith, EndsWith, Regex }
public enum NumberMatchMode { Equals, Between, GreaterThan, LessThan }
public enum DateMatchMode { Equals, Between, Weekday, Month }

// ????????????????????????????????????????????
// Condition Tree (JSON polymorphic)
// ????????????????????????????????????????????

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextCondition), "Text")]
[JsonDerivedType(typeof(AmountCondition), "Amount")]
[JsonDerivedType(typeof(CurrencyCondition), "Currency")]
[JsonDerivedType(typeof(DateCondition), "Date")]
[JsonDerivedType(typeof(AndCondition), "And")]
[JsonDerivedType(typeof(OrCondition), "Or")]
[JsonDerivedType(typeof(NotCondition), "Not")]
public abstract class ImportCondition { }

public sealed class TextCondition : ImportCondition
{
    public string Field { get; set; } = "Description";
    public StringMatchMode Mode { get; set; } = StringMatchMode.Contains;
    public string Value { get; set; } = "";
    public bool IgnoreCase { get; set; } = true;
}

public sealed class AmountCondition : ImportCondition
{
    public NumberMatchMode Mode { get; set; } = NumberMatchMode.Equals;
    public decimal Value1 { get; set; }
    public decimal Value2 { get; set; }
    public decimal ToleranceAbs { get; set; }
    public decimal TolerancePct { get; set; }
}

public sealed class CurrencyCondition : ImportCondition
{
    public string Code { get; set; } = "";
}

public sealed class DateCondition : ImportCondition
{
    public DateMatchMode Mode { get; set; } = DateMatchMode.Equals;
    public DateOnly? Date1 { get; set; }
    public DateOnly? Date2 { get; set; }
}

public sealed class AndCondition : ImportCondition
{
    public List<ImportCondition> Children { get; set; } = new();
}

public sealed class OrCondition : ImportCondition
{
    public List<ImportCondition> Children { get; set; } = new();
}

public sealed class NotCondition : ImportCondition
{
    public ImportCondition? Child { get; set; }
}

// ????????????????????????????????????????????
// Actions (JSON polymorphic)
// ????????????????????????????????????????????

[JsonPolymorphic(TypeDiscriminatorPropertyName = "actionType")]
[JsonDerivedType(typeof(CategorizeAction), "Categorize")]
[JsonDerivedType(typeof(RouteAccountAction), "RouteAccount")]
[JsonDerivedType(typeof(IgnoreAction), "Ignore")]
[JsonDerivedType(typeof(MatchBillAction), "MatchBill")]
[JsonDerivedType(typeof(MatchInvoiceAction), "MatchInvoice")]
[JsonDerivedType(typeof(MatchTransferAction), "MatchTransfer")]
[JsonDerivedType(typeof(MatchObligationPaymentAction), "MatchObligationPayment")]
public abstract class ImportRuleAction { }

public sealed class CategorizeAction : ImportRuleAction
{
    public string CategoryName { get; set; } = "";
    public string? NotesTemplate { get; set; }
    public List<string>? TagList { get; set; }
}

public sealed class RouteAccountAction : ImportRuleAction
{
    public Guid AccountId { get; set; }
}

public sealed class IgnoreAction : ImportRuleAction
{
    public string Reason { get; set; } = "";
}

public sealed class MatchBillAction : ImportRuleAction
{
    public string MatchMode { get; set; } = "NearestOutstanding";
    public decimal Tolerance { get; set; }
}

public sealed class MatchInvoiceAction : ImportRuleAction
{
    public string MatchMode { get; set; } = "NearestOutstanding";
    public decimal Tolerance { get; set; }
}

public sealed class MatchTransferAction : ImportRuleAction
{
    public string Direction { get; set; } = "Either";
    public decimal Tolerance { get; set; }
}

public sealed class MatchObligationPaymentAction : ImportRuleAction
{
    public Guid? ObligationId { get; set; }
    public decimal Tolerance { get; set; }
}

// ????????????????????????????????????????????
// Records
// ????????????????????????????????????????????

public sealed class ImportRuleRecord
{
    public Guid PackId { get; set; }
    public Guid RuleId { get; set; }
    public int Version { get; set; }
    public string Kind { get; set; } = "";
    public int Priority { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsArchived { get; set; }
    public string MatchSpecJson { get; set; } = "{}";
    public string ActionSpecJson { get; set; } = "{}";
    public DateOnly CreatedDate { get; set; }
}

public sealed class ImportRulePackRecord
{
    public Guid PackId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsEnabled { get; set; }
    public bool IsArchived { get; set; }
}

// ????????????????????????????????????????????
// Suggestion / Explanation
// ????????????????????????????????????????????

public sealed record ImportRuleExplain(
    Guid RuleId,
    int Version,
    Guid PackId,
    string Kind,
    int Priority,
    string ExplanationText
);

public enum SuggestionKind
{
    Categorize,
    ApplyExpense,
    ApplyIncome,
    ApplyTransfer,
    PayBill,
    ReceiveInvoice,
    Ignore,
    MatchOnly
}

public sealed class ImportSuggestion
{
    public Guid ImportedTransactionId { get; set; }
    public SuggestionKind Kind { get; set; }
    public int Confidence { get; set; }
    public Guid? ProposedAccountId { get; set; }
    public string? ProposedCategory { get; set; }
    public Guid? ProposedRelatedEntityId { get; set; }
    public string? Notes { get; set; }
    public List<ImportRuleExplain> Explain { get; set; } = new();
    public string DeterministicSuggestionId { get; set; } = "";

    public static string ComputeSuggestionId(Guid importedId, Guid ruleId, int version, string actionFingerprint)
    {
        var input = $"{importedId}|{ruleId}|{version}|{actionFingerprint}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
