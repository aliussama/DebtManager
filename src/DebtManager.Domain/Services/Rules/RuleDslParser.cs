using System.Text.Json;
using DebtManager.Domain.Rules;

namespace DebtManager.Domain.Services.Rules;

/// <summary>
/// Parser for rule DSL JSON format.
/// Supports grace periods, interest accrual, and complex predicates.
/// </summary>
public static class RuleDslParser
{
    /// <summary>
    /// Parse a rule pack from JSON.
    /// </summary>
    public static ParsedRulePack Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var packId = root.GetProperty("pack_id").GetString() ?? throw new InvalidOperationException("pack_id required");
        var version = root.GetProperty("version").GetString() ?? "1.0";
        var effectiveFrom = ParseDateOnly(root, "effective_from");
        var effectiveTo = ParseDateOnlyNullable(root, "effective_to");

        var rules = new List<ParsedRule>();

        if (root.TryGetProperty("rules", out var rulesArray))
        {
            foreach (var ruleEl in rulesArray.EnumerateArray())
            {
                rules.Add(ParseRule(ruleEl));
            }
        }

        return new ParsedRulePack(
            PackId: packId,
            Version: version,
            EffectiveFrom: effectiveFrom,
            EffectiveTo: effectiveTo,
            Rules: rules.AsReadOnly()
        );
    }

    private static ParsedRule ParseRule(JsonElement el)
    {
        var id = el.GetProperty("id").GetString() ?? Guid.NewGuid().ToString();
        var type = el.GetProperty("type").GetString() ?? "charge";
        var priority = el.TryGetProperty("priority", out var p) ? p.GetInt32() : 100;
        var phase = el.TryGetProperty("phase", out var ph) ? ph.GetString() ?? "fee" : DeterminePhase(type);
        var enabled = !el.TryGetProperty("enabled", out var en) || en.GetBoolean();

        ParsedPredicate? predicate = null;
        if (el.TryGetProperty("when", out var whenEl))
        {
            predicate = ParsePredicate(whenEl);
        }

        ParsedEffect? effect = null;
        if (el.TryGetProperty("effect", out var effectEl))
        {
            effect = ParseEffect(effectEl, type);
        }

        return new ParsedRule(
            Id: id,
            Type: type,
            Phase: phase,
            Priority: priority,
            Enabled: enabled,
            Predicate: predicate,
            Effect: effect
        );
    }

    private static ParsedPredicate ParsePredicate(JsonElement el)
    {
        if (el.TryGetProperty("all", out var allEl))
        {
            var conditions = allEl.EnumerateArray()
                .Select(ParseCondition)
                .ToList();
            return new ParsedPredicate(LogicalOperator.All, conditions);
        }

        if (el.TryGetProperty("any", out var anyEl))
        {
            var conditions = anyEl.EnumerateArray()
                .Select(ParseCondition)
                .ToList();
            return new ParsedPredicate(LogicalOperator.Any, conditions);
        }

        // Single condition
        return new ParsedPredicate(LogicalOperator.All, new[] { ParseCondition(el) });
    }

    private static ParsedCondition ParseCondition(JsonElement el)
    {
        var field = el.GetProperty("field").GetString() ?? "";
        var op = el.GetProperty("op").GetString() ?? "==";

        object? value = null;
        if (el.TryGetProperty("value", out var valEl))
        {
            value = valEl.ValueKind switch
            {
                JsonValueKind.Number => valEl.TryGetInt32(out var i) ? i : valEl.GetDecimal(),
                JsonValueKind.String => valEl.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }

        return new ParsedCondition(field, op, value);
    }

    private static ParsedEffect ParseEffect(JsonElement el, string ruleType)
    {
        // Handle nested effect types
        if (el.TryGetProperty("add_charge", out var chargeEl))
        {
            return ParseChargeEffect(chargeEl, RuleEffectTypes.AddCharge);
        }

        if (el.TryGetProperty("accrue_interest", out var interestEl))
        {
            return ParseInterestEffect(interestEl);
        }

        if (el.TryGetProperty("apply_grace", out var graceEl))
        {
            return ParseGraceEffect(graceEl);
        }

        if (el.TryGetProperty("apply_penalty", out var penaltyEl))
        {
            return ParseChargeEffect(penaltyEl, RuleEffectTypes.ApplyPenalty);
        }

        // Direct effect properties
        return ParseChargeEffect(el, DetermineEffectType(ruleType));
    }

    private static ParsedEffect ParseChargeEffect(JsonElement el, string effectType)
    {
        var data = new Dictionary<string, object>();

        if (el.TryGetProperty("amount", out var amountEl))
        {
            data[RuleEffectFields.Amount] = amountEl.ValueKind == JsonValueKind.Number
                ? amountEl.GetDecimal()
                : Convert.ToDecimal(amountEl.GetString());
        }

        if (el.TryGetProperty("label", out var labelEl))
        {
            data[RuleEffectFields.Label] = labelEl.GetString() ?? "";
        }

        if (el.TryGetProperty("chargeType", out var ctEl))
        {
            data[RuleEffectFields.ChargeType] = ctEl.GetString() ?? "";
        }

        if (el.TryGetProperty("penaltyType", out var ptEl))
        {
            data[RuleEffectFields.PenaltyType] = ptEl.GetString() ?? "";
        }

        if (el.TryGetProperty("maxPenalty", out var maxEl))
        {
            data[RuleEffectFields.MaxPenalty] = maxEl.GetDecimal();
        }

        return new ParsedEffect(effectType, data);
    }

    private static ParsedEffect ParseInterestEffect(JsonElement el)
    {
        var data = new Dictionary<string, object>();

        if (el.TryGetProperty("rate", out var rateEl))
        {
            data[RuleEffectFields.Rate] = rateEl.GetDecimal();
        }

        if (el.TryGetProperty("compounding", out var compEl))
        {
            data[RuleEffectFields.Compounding] = compEl.GetString() ?? "daily";
        }

        if (el.TryGetProperty("basis", out var basisEl))
        {
            data[RuleEffectFields.DayCountBasis] = basisEl.GetString() ?? "actual365";
        }

        if (el.TryGetProperty("label", out var labelEl))
        {
            data[RuleEffectFields.Label] = labelEl.GetString() ?? "Interest";
        }

        return new ParsedEffect(RuleEffectTypes.AccrueInterest, data);
    }

    private static ParsedEffect ParseGraceEffect(JsonElement el)
    {
        var data = new Dictionary<string, object>();

        if (el.TryGetProperty("days", out var daysEl))
        {
            data[RuleEffectFields.GraceDays] = daysEl.GetInt32();
        }

        if (el.TryGetProperty("type", out var typeEl))
        {
            data[RuleEffectFields.GraceType] = typeEl.GetString() ?? "calendar";
        }

        if (el.TryGetProperty("appliesToPenalties", out var penEl))
        {
            data[RuleEffectFields.AppliesToPenalties] = penEl.GetBoolean();
        }

        if (el.TryGetProperty("appliesToInterest", out var intEl))
        {
            data[RuleEffectFields.AppliesToInterest] = intEl.GetBoolean();
        }

        return new ParsedEffect(RuleEffectTypes.ApplyGrace, data);
    }

    private static DateOnly ParseDateOnly(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var el))
        {
            return DateOnly.Parse(el.GetString() ?? "2000-01-01");
        }
        return DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private static DateOnly? ParseDateOnlyNullable(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var el) && el.ValueKind != JsonValueKind.Null)
        {
            var str = el.GetString();
            if (!string.IsNullOrEmpty(str))
            {
                return DateOnly.Parse(str);
            }
        }
        return null;
    }

    private static string DeterminePhase(string ruleType)
    {
        return ruleType.ToLowerInvariant() switch
        {
            "grace" => "grace",
            "interest" => "interest",
            "penalty" => "penalty",
            "fee" => "fee",
            "tax" => "tax",
            _ => "fee"
        };
    }

    private static string DetermineEffectType(string ruleType)
    {
        return ruleType.ToLowerInvariant() switch
        {
            "grace" => RuleEffectTypes.ApplyGrace,
            "interest" => RuleEffectTypes.AccrueInterest,
            "penalty" => RuleEffectTypes.ApplyPenalty,
            "fee" => RuleEffectTypes.ApplyFee,
            "tax" => RuleEffectTypes.ApplyTax,
            _ => RuleEffectTypes.AddCharge
        };
    }
}

/// <summary>
/// Parsed rule pack from DSL.
/// </summary>
public sealed record ParsedRulePack(
    string PackId,
    string Version,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    IReadOnlyList<ParsedRule> Rules
);

/// <summary>
/// Parsed rule from DSL.
/// </summary>
public sealed record ParsedRule(
    string Id,
    string Type,
    string Phase,
    int Priority,
    bool Enabled,
    ParsedPredicate? Predicate,
    ParsedEffect? Effect
);

/// <summary>
/// Parsed predicate (when clause).
/// </summary>
public sealed record ParsedPredicate(
    LogicalOperator Operator,
    IReadOnlyList<ParsedCondition> Conditions
);

/// <summary>
/// Logical operator for combining conditions.
/// </summary>
public enum LogicalOperator
{
    All,
    Any
}

/// <summary>
/// Parsed condition from DSL.
/// </summary>
public sealed record ParsedCondition(
    string Field,
    string Operator,
    object? Value
);

/// <summary>
/// Parsed effect from DSL.
/// </summary>
public sealed record ParsedEffect(
    string EffectType,
    IReadOnlyDictionary<string, object> Data
);