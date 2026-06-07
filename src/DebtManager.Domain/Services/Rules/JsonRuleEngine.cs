using DebtManager.Domain.Rules;

namespace DebtManager.Domain.Services.Rules;

// Evaluates a RulePackVersion against RuleEvaluationContext (Facts)
// Pure domain logic: no DB, no UI.
public sealed class JsonRuleEngine
{
    public (IReadOnlyList<RuleEffect> Effects, IReadOnlyList<string> FiredKeys, Dictionary<string, object> Debug)
        Evaluate(RulePackVersion version, RuleEvaluationContext ctx)
    {
        var effects = new List<RuleEffect>();
        var fired = new List<string>();
        var debug = new Dictionary<string, object>();

        foreach (var rule in version.Rules)
        {
            if (!Matches(rule.When, ctx.Facts))
                continue;

            fired.Add(rule.RuleKey);

            foreach (var t in rule.Then)
            {
                effects.Add(new RuleEffect(t.EffectType, t.Data));
            }
        }

        debug["rules_evaluated"] = version.Rules.Count;
        debug["rules_fired"] = fired.Count;

        return (effects, fired, debug);
    }

    private static bool Matches(RuleWhen when, IReadOnlyDictionary<string, object> facts)
    {
        var results = when.Predicates.Select(p => EvalPredicate(p, facts)).ToList();

        return when.Op.ToLowerInvariant() switch
        {
            "and" => results.All(x => x),
            "or" => results.Any(x => x),
            _ => results.All(x => x)
        };
    }

    private static bool EvalPredicate(RulePredicate p, IReadOnlyDictionary<string, object> facts)
    {
        if (!facts.TryGetValue(p.Fact, out var factVal) || factVal is null) return false;

        // Normalize types
        var cmp = p.Compare.ToLowerInvariant();

        // numeric compare (int/decimal)
        if (TryDecimal(factVal, out var fd) && TryDecimal(p.Value, out var vd))
        {
            return cmp switch
            {
                "gt" => fd > vd,
                "gte" => fd >= vd,
                "lt" => fd < vd,
                "lte" => fd <= vd,
                "eq" => fd == vd,
                "neq" => fd != vd,
                _ => false
            };
        }

        // date compare
        if (TryDateOnly(factVal, out var fdate) && TryDateOnly(p.Value, out var vdate))
        {
            return cmp switch
            {
                "gt" => fdate > vdate,
                "gte" => fdate >= vdate,
                "lt" => fdate < vdate,
                "lte" => fdate <= vdate,
                "eq" => fdate == vdate,
                "neq" => fdate != vdate,
                _ => false
            };
        }

        // string compare
        var fs = Convert.ToString(factVal) ?? "";
        var vs = Convert.ToString(p.Value) ?? "";
        return cmp switch
        {
            "eq" => string.Equals(fs, vs, StringComparison.OrdinalIgnoreCase),
            "neq" => !string.Equals(fs, vs, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool TryDecimal(object v, out decimal d)
    {
        d = 0m;
        try { d = Convert.ToDecimal(v); return true; } catch { return false; }
    }

    private static bool TryDateOnly(object v, out DateOnly d)
    {
        d = default;
        if (v is DateOnly dd) { d = dd; return true; }
        var s = Convert.ToString(v);
        return !string.IsNullOrWhiteSpace(s) && DateOnly.TryParse(s, out d);
    }
}
