using System.Text.Json;
using DebtManager.Domain.Rules;
using DebtManager.Domain.Services.Rules;

namespace DebtManager.Infrastructure.Rules;

public sealed class SqliteRuleEngine : IRuleEngine
{
    private readonly IRulePackRepository _repo;
    private readonly IRulePackResolver _resolver;

    public SqliteRuleEngine(IRulePackRepository repo, IRulePackResolver resolver)
    {
        _repo = repo;
        _resolver = resolver;
    }

    public async Task<(IReadOnlyList<RuleEffect> Effects, RuleTrace Trace)> EvaluateAsync(
        RuleEvaluationContext ctx,
        CancellationToken ct)
    {
        var packId = await _resolver.ResolveRulePackIdAsync(ctx.ObligationId, ctx.EvaluationDate, ct);
        if (string.IsNullOrWhiteSpace(packId))
        {
            var empty = new RuleTrace(new RulePackVersionId(Guid.Empty), Array.Empty<string>(), new Dictionary<string, object>
            {
                ["reason"] = "no_rule_pack_assigned"
            });
            return (Array.Empty<RuleEffect>(), empty);
        }

        var version = await _repo.GetActiveVersionAsync(packId, ctx.EvaluationDate, ct);
        if (version is null)
        {
            var empty = new RuleTrace(new RulePackVersionId(Guid.Empty), Array.Empty<string>(), new Dictionary<string, object>
            {
                ["reason"] = "no_active_version",
                ["rulePackId"] = packId
            });
            return (Array.Empty<RuleEffect>(), empty);
        }

        // v1 DSL: stored as { "rules":[ { "key":"...", "when":{...}, "effect":{...} } ] }
        var doc = JsonDocument.Parse(version.RulesJson);
        if (!doc.RootElement.TryGetProperty("rules", out var rulesEl) || rulesEl.ValueKind != JsonValueKind.Array)
        {
            var bad = new RuleTrace(new RulePackVersionId(version.RulePackVersionId), Array.Empty<string>(), new Dictionary<string, object>
            {
                ["reason"] = "invalid_rules_json"
            });
            return (Array.Empty<RuleEffect>(), bad);
        }

        var effects = new List<RuleEffect>();
        var fired = new List<string>();

        foreach (var ruleEl in rulesEl.EnumerateArray())
        {
            var key = ruleEl.GetProperty("key").GetString() ?? "unknown";
            if (!ruleEl.TryGetProperty("when", out var whenEl)) continue;
            if (!ruleEl.TryGetProperty("effect", out var effectEl)) continue;

            if (!EvaluateWhen(ctx, whenEl)) continue;

            fired.Add(key);

            // effect: { "add_charge": { "amount": 100, "label":"Late", "chargeType":"penalty" } }
            if (effectEl.TryGetProperty("add_charge", out var addCharge))
            {
                var amount = addCharge.GetProperty("amount").GetDecimal();
                var label = addCharge.GetProperty("label").GetString() ?? "Charge";
                var chargeType = addCharge.TryGetProperty("chargeType", out var ctEl) ? (ctEl.GetString() ?? "other") : "other";

                effects.Add(new RuleEffect("add_charge", new Dictionary<string, object>
                {
                    ["amount"] = amount,
                    ["label"] = label,
                    ["chargeType"] = chargeType,
                    ["ruleKey"] = key
                }));
            }
        }

        var trace = new RuleTrace(
            new RulePackVersionId(version.RulePackVersionId),
            fired.AsReadOnly(),
            new Dictionary<string, object>
            {
                ["rulePackId"] = version.RulePackId,
                ["versionLabel"] = version.VersionLabel
            });


        return (effects.AsReadOnly(), trace);
    }

    private static bool EvaluateWhen(RuleEvaluationContext ctx, JsonElement whenEl)
    {
        // v1: supports { "all":[{"fact":"installment.days_overdue","op":">","value":0}, ...] }
        if (whenEl.TryGetProperty("all", out var allEl) && allEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var cond in allEl.EnumerateArray())
                if (!EvalCond(ctx, cond)) return false;
            return true;
        }

        if (whenEl.TryGetProperty("any", out var anyEl) && anyEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var cond in anyEl.EnumerateArray())
                if (EvalCond(ctx, cond)) return true;
            return false;
        }

        // single condition
        return EvalCond(ctx, whenEl);
    }

    private static bool EvalCond(RuleEvaluationContext ctx, JsonElement cond)
    {
        var fact = cond.GetProperty("fact").GetString()!;
        var op = cond.GetProperty("op").GetString()!;
        var valueEl = cond.GetProperty("value");

        if (!ctx.Facts.TryGetValue(fact, out var factObj)) return false;

        // numeric comparison
        if (factObj is int i)
        {
            var v = valueEl.GetInt32();
            return op switch
            {
                ">" => i > v,
                ">=" => i >= v,
                "<" => i < v,
                "<=" => i <= v,
                "==" => i == v,
                "!=" => i != v,
                _ => false
            };
        }

        if (factObj is decimal d)
        {
            var v = valueEl.GetDecimal();
            return op switch
            {
                ">" => d > v,
                ">=" => d >= v,
                "<" => d < v,
                "<=" => d <= v,
                "==" => d == v,
                "!=" => d != v,
                _ => false
            };
        }

        // string compare
        var s = factObj.ToString() ?? "";
        var vs = valueEl.ValueKind == JsonValueKind.String ? valueEl.GetString()! : valueEl.ToString();

        return op switch
        {
            "==" => s == vs,
            "!=" => s != vs,
            _ => false
        };
    }
}
