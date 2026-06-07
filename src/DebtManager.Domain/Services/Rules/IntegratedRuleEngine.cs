using DebtManager.Domain.Rules;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Services.Rules;

/// <summary>
/// Integrated rule engine that combines JSON rule loading, evaluation, and tracing.
/// Implements IRuleEngine for use with TracingRuleEngine wrapper.
/// </summary>
public sealed class IntegratedRuleEngine : IRuleEngine
{
    private readonly RulePackLoader _loader = new();
    private readonly JsonRuleEngine _evaluator = new();
    private readonly Func<string, DateOnly, Task<RulePackVersion?>> _versionResolver;

    /// <summary>
    /// Create with a resolver function that loads rule pack JSON by ID and date.
    /// </summary>
    public IntegratedRuleEngine(Func<string, DateOnly, Task<string?>> rulePackResolver)
    {
        _versionResolver = async (packId, asOf) =>
        {
            var json = await rulePackResolver(packId, asOf);
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                return _loader.LoadVersion(json);
            }
            catch
            {
                return null;
            }
        };
    }

    /// <summary>
    /// Create with a static set of rule packs (for testing).
    /// </summary>
    public IntegratedRuleEngine(IReadOnlyDictionary<string, RulePack> rulePacks)
    {
        _versionResolver = (packId, asOf) =>
        {
            if (rulePacks.TryGetValue(packId, out var pack))
            {
                var version = pack.Versions
                    .Where(v => v.EffectiveFrom <= asOf && (!v.EffectiveTo.HasValue || v.EffectiveTo.Value >= asOf))
                    .Where(v => v.Status == "active")
                    .OrderByDescending(v => v.EffectiveFrom)
                    .FirstOrDefault();

                return Task.FromResult(version);
            }
            return Task.FromResult<RulePackVersion?>(null);
        };
    }

    /// <summary>
    /// Evaluate rules for a given context.
    /// </summary>
    public async Task<(IReadOnlyList<RuleEffect> Effects, RuleTrace Trace)> EvaluateAsync(
        RuleEvaluationContext ctx,
        CancellationToken ct)
    {
        // Get the rule pack ID from facts or use default
        var rulePackId = ctx.Facts.TryGetValue("rule_pack_id", out var packIdObj)
            ? packIdObj?.ToString() ?? "default"
            : "default";

        // Load the rule pack version
        var version = await _versionResolver(rulePackId, ctx.EvaluationDate);

        if (version == null)
        {
            // No rules found - return empty result
            return (
                Effects: Array.Empty<RuleEffect>(),
                Trace: new RuleTrace(
                    new RulePackVersionId(Guid.Empty),
                    Array.Empty<string>(),
                    new Dictionary<string, object> { ["error"] = $"Rule pack '{rulePackId}' not found" }
                )
            );
        }

        // Evaluate rules
        var (effects, firedKeys, debug) = _evaluator.Evaluate(version, ctx);

        // Build trace
        var trace = new RuleTrace(
            VersionId: new RulePackVersionId(version.VersionId),
            FiredRuleKeys: firedKeys,
            Debug: debug
        );

        return (effects, trace);
    }
}

/// <summary>
/// Factory for creating rule engines with different configurations.
/// </summary>
public static class RuleEngineFactory
{
    /// <summary>
    /// Create a no-op rule engine (for testing/development).
    /// </summary>
    public static IRuleEngine CreateNoOp()
    {
        return new NoOpRuleEngine();
    }

    /// <summary>
    /// Create an integrated rule engine with a resolver function.
    /// </summary>
    public static IRuleEngine CreateIntegrated(Func<string, DateOnly, Task<string?>> resolver)
    {
        return new IntegratedRuleEngine(resolver);
    }

    /// <summary>
    /// Create an integrated rule engine with static rule packs.
    /// </summary>
    public static IRuleEngine CreateIntegrated(IReadOnlyDictionary<string, RulePack> rulePacks)
    {
        return new IntegratedRuleEngine(rulePacks);
    }

    /// <summary>
    /// Create a tracing rule engine that wraps another engine.
    /// </summary>
    public static TracingRuleEngine CreateWithTracing(IRuleEngine inner)
    {
        return new TracingRuleEngine(inner);
    }
}
