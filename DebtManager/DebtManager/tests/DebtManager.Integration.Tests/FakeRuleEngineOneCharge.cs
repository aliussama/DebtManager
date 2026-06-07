using DebtManager.Domain.Rules;

namespace DebtManager.Integration.Tests;

public sealed class FakeRuleEngineOneCharge : IRuleEngine
{
    public Task<(IReadOnlyList<RuleEffect> Effects, RuleTrace Trace)> EvaluateAsync(
        RuleEvaluationContext ctx,
        CancellationToken ct)
    {
        var data = new Dictionary<string, object>
        {
            ["amount"] = 50m,
            ["label"] = "Fake Penalty",
            ["chargeType"] = "penalty",
            ["ruleKey"] = "fake.penalty"
        };

        var effects = new[] { new RuleEffect("Charge", data) };

        var trace = new RuleTrace(
            VersionId: new RulePackVersionId(Guid.Empty),
            FiredRuleKeys: new[] { "fake.penalty" },
            Debug: new Dictionary<string, object>()
        );

        return Task.FromResult(((IReadOnlyList<RuleEffect>)effects, trace));
    }
}
