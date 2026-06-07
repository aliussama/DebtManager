using DebtManager.Domain.Rules;

namespace DebtManager.Domain.Services.Rules;

// Reference implementation used for v1 tests and as an example.
// Real bank packs will live in RulePack storage later.
public sealed class ReferencePenaltyRuleEngine : IRuleEngine
{
    private readonly int _graceDays;
    private readonly decimal _fixedPenaltyAmount;

    public ReferencePenaltyRuleEngine(int graceDays, decimal fixedPenaltyAmount)
    {
        _graceDays = graceDays;
        _fixedPenaltyAmount = fixedPenaltyAmount;
    }

    public Task<(IReadOnlyList<RuleEffect> Effects, RuleTrace Trace)> EvaluateAsync(
        RuleEvaluationContext ctx,
        CancellationToken ct)
    {
        var effects = new List<RuleEffect>();

        // Must have days_overdue + outstanding_amount
        if (!TryGetInt(ctx.Facts, RuleFactKeys.DaysOverdue, out var daysOverdue))
            return Task.FromResult(Result(effects));

        if (!TryGetDecimal(ctx.Facts, RuleFactKeys.OutstandingAmount, out var outstanding))
            return Task.FromResult(Result(effects));

        if (outstanding <= 0m) return Task.FromResult(Result(effects));

        // Apply penalty only if overdue beyond grace
        if (daysOverdue > _graceDays)
        {
            effects.Add(new RuleEffect(
                EffectType: RuleEffectTypes.Charge,
                Data: new Dictionary<string, object>
                {
                    { RuleEffectFields.Amount, _fixedPenaltyAmount },
                    { RuleEffectFields.Label, $"Late penalty (>{_graceDays}d grace)" },
                    { RuleEffectFields.ChargeType, "penalty" },
                    { RuleEffectFields.RuleKey, "penalty.fixed.v1" }
                }
            ));
        }

        return Task.FromResult(Result(effects));
    }

    private static (IReadOnlyList<RuleEffect>, RuleTrace) Result(List<RuleEffect> effects)
        => (effects, new RuleTrace(
            VersionId: new RulePackVersionId(Guid.NewGuid()),
            FiredRuleKeys: effects.Count > 0 ? new List<string> { "penalty.fixed.v1" } : new List<string>(),
            Debug: new Dictionary<string, object>()
        ));

    private static bool TryGetInt(IReadOnlyDictionary<string, object> facts, string key, out int value)
    {
        value = 0;
        if (!facts.TryGetValue(key, out var obj) || obj is null) return false;
        try { value = Convert.ToInt32(obj); return true; } catch { return false; }
    }

    private static bool TryGetDecimal(IReadOnlyDictionary<string, object> facts, string key, out decimal value)
    {
        value = 0m;
        if (!facts.TryGetValue(key, out var obj) || obj is null) return false;
        try { value = Convert.ToDecimal(obj); return true; } catch { return false; }
    }
}
