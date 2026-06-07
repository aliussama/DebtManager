namespace DebtManager.Domain.Rules;

// Root pack (one bank/product)
public sealed record RulePack(
    string PackId,                 // e.g. "CIB_CreditCard", "NBE_Loan"
    string DisplayName,
    string CountryCode,            // "EG"
    string CurrencyCode,           // default currency (can be overridden per ctx)
    IReadOnlyList<RulePackVersion> Versions
);

public sealed record RulePackVersion(
    Guid VersionId,
    string VersionLabel,           // "2026-01 v1"
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Status,                 // "active"|"deprecated"|"draft"
    IReadOnlyList<RuleDefinition> Rules
);

public sealed record RuleDefinition(
    string RuleKey,                // unique within pack
    string Description,
    RuleWhen When,
    IReadOnlyList<RuleThen> Then
);

// Conditions (AND/OR)
public sealed record RuleWhen(
    string Op,                     // "and"|"or"
    IReadOnlyList<RulePredicate> Predicates
);

// Single predicate: fact comparison
public sealed record RulePredicate(
    string Fact,                   // RuleFactKeys.*
    string Compare,                // "gt"|"gte"|"lt"|"lte"|"eq"|"neq"
    object Value                   // number/string/bool/date
);

// Effects
public sealed record RuleThen(
    string EffectType,             // RuleEffectTypes.*
    IReadOnlyDictionary<string, object> Data
);
