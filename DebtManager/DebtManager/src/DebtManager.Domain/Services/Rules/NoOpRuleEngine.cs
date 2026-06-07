using DebtManager.Domain.Rules;

namespace DebtManager.Domain.Services.Rules;

public sealed class NoOpRuleEngine : IRuleEngine
{
    public Task<(IReadOnlyList<RuleEffect> Effects, RuleTrace Trace)> EvaluateAsync(
        RuleEvaluationContext ctx,
        CancellationToken ct)
    {
        var trace = new RuleTrace(
            VersionId: new RulePackVersionId(Guid.Empty),
            FiredRuleKeys: Array.Empty<string>(),
            Debug: new Dictionary<string, object>()
        );

        return Task.FromResult((
            (IReadOnlyList<RuleEffect>)Array.Empty<RuleEffect>(),
            trace
        ));
    }
}