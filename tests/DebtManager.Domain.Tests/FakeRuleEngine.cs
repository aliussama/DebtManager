using DebtManager.Domain.Rules;

namespace DebtManager.Domain.Tests;

public sealed class FakeRuleEngine : IRuleEngine
{
    public Task<(IReadOnlyList<RuleEffect> Effects, RuleTrace Trace)> EvaluateAsync(
        RuleEvaluationContext ctx,
        CancellationToken ct)
    {
        // Example: if overdue, apply a penalty of 100 EGP
        var daysOverdue = ctx.Facts.TryGetValue("installment.days_overdue", out var v)
            ? Convert.ToInt32(v)
            : 0;

        if (daysOverdue > 0)
        {
            var effects = new[]
            {
                new RuleEffect("add_charge", new Dictionary<string, object>
                {
                    ["amount"] = 100m,
                    ["label"] = "Late Penalty",
                    ["chargeType"] = "penalty",
                    ["ruleKey"] = "late_penalty_v1"
                })
            };

            var trace = new RuleTrace(
                new RulePackVersionId(Guid.NewGuid()),
                new[] { "late_penalty_v1" },
                new Dictionary<string, object> { ["why"] = "days_overdue > 0" });

            return Task.FromResult(((IReadOnlyList<RuleEffect>)effects, trace));
        }

        var emptyTrace = new RuleTrace(
            new RulePackVersionId(Guid.NewGuid()),
            Array.Empty<string>(),
            new Dictionary<string, object>());

        return Task.FromResult(((IReadOnlyList<RuleEffect>)Array.Empty<RuleEffect>(), emptyTrace));
    }
}
