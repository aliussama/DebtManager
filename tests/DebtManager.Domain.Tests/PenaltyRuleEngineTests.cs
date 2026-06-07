using DebtManager.Domain.Rules;
using DebtManager.Domain.Services.Rules;
using Xunit;

namespace DebtManager.Domain.Tests;

public class PenaltyRuleEngineTests
{
    [Fact]
    public async Task Penalty_DoesNotFire_InsideGrace()
    {
        var engine = new ReferencePenaltyRuleEngine(graceDays: 10, fixedPenaltyAmount: 50m);

        var ctx = new RuleEvaluationContext(
            EvaluationDate: new DateOnly(2026, 1, 20),
            ObligationId: Guid.NewGuid(),
            InstallmentKey: Guid.NewGuid(),
            CurrencyCode: "EGP",
            Facts: new Dictionary<string, object>
            {
                { RuleFactKeys.DaysOverdue, 10 }, // exactly grace => still no penalty
                { RuleFactKeys.OutstandingAmount, 1000m },
                { RuleFactKeys.OutstandingCurrency, "EGP" }
            }
        );

        var (effects, trace) = await engine.EvaluateAsync(ctx, CancellationToken.None);

        Assert.Empty(effects);
        Assert.Empty(trace.FiredRuleKeys);
    }

    [Fact]
    public async Task Penalty_Fires_AfterGrace()
    {
        var engine = new ReferencePenaltyRuleEngine(graceDays: 10, fixedPenaltyAmount: 50m);

        var ctx = new RuleEvaluationContext(
            EvaluationDate: new DateOnly(2026, 1, 22),
            ObligationId: Guid.NewGuid(),
            InstallmentKey: Guid.NewGuid(),
            CurrencyCode: "EGP",
            Facts: new Dictionary<string, object>
            {
                { RuleFactKeys.DaysOverdue, 11 },
                { RuleFactKeys.OutstandingAmount, 1000m },
                { RuleFactKeys.OutstandingCurrency, "EGP" }
            }
        );

        var (effects, trace) = await engine.EvaluateAsync(ctx, CancellationToken.None);

        Assert.Single(effects);
        Assert.Contains(trace.FiredRuleKeys, k => k == "penalty.fixed.v1");
        Assert.Equal(RuleEffectTypes.Charge, effects[0].EffectType);
    }
}
