using DebtManager.Domain.Rules;
using DebtManager.Domain.Services.Rules;
using Xunit;

namespace DebtManager.Domain.Tests.Rules;

public class IntegratedRuleEngineTests
{
    [Fact]
    public async Task EvaluateAsync_WithOverdueInstallment_FiresGraceAndPenaltyRules()
    {
        // Arrange
        var loader = new RulePackLoader();
        var pack = loader.Load(SampleRulePacks.BasicLoan);
        var rulePacks = new Dictionary<string, RulePack> { ["basic_loan"] = pack };

        var engine = new IntegratedRuleEngine(rulePacks);

        var ctx = new RuleEvaluationContext(
            EvaluationDate: new DateOnly(2025, 6, 15),
            ObligationId: Guid.NewGuid(),
            InstallmentKey: Guid.NewGuid(),
            CurrencyCode: "EGP",
            Facts: new Dictionary<string, object>
            {
                ["rule_pack_id"] = "basic_loan",
                ["installment.is_overdue"] = true,
                ["installment.days_overdue"] = 10 // Beyond 7-day grace
            }
        );

        // Act
        var (effects, trace) = await engine.EvaluateAsync(ctx, CancellationToken.None);

        // Assert
        Assert.NotEmpty(effects);
        Assert.Contains(effects, e => e.EffectType == RuleEffectTypes.ApplyGrace);
        Assert.Contains(effects, e => e.EffectType == RuleEffectTypes.ApplyPenalty);
        Assert.Equal(2, trace.FiredRuleKeys.Count);
    }

    [Fact]
    public async Task EvaluateAsync_WithinGracePeriod_OnlyFiresGraceRule()
    {
        // Arrange
        var loader = new RulePackLoader();
        var pack = loader.Load(SampleRulePacks.BasicLoan);
        var rulePacks = new Dictionary<string, RulePack> { ["basic_loan"] = pack };

        var engine = new IntegratedRuleEngine(rulePacks);

        var ctx = new RuleEvaluationContext(
            EvaluationDate: new DateOnly(2025, 6, 15),
            ObligationId: Guid.NewGuid(),
            InstallmentKey: Guid.NewGuid(),
            CurrencyCode: "EGP",
            Facts: new Dictionary<string, object>
            {
                ["rule_pack_id"] = "basic_loan",
                ["installment.is_overdue"] = true,
                ["installment.days_overdue"] = 5 // Within 7-day grace
            }
        );

        // Act
        var (effects, trace) = await engine.EvaluateAsync(ctx, CancellationToken.None);

        // Assert
        Assert.Single(effects);
        Assert.Equal(RuleEffectTypes.ApplyGrace, effects[0].EffectType);
        Assert.Single(trace.FiredRuleKeys);
        Assert.Equal("grace_period_7_days", trace.FiredRuleKeys[0]);
    }

    [Fact]
    public async Task EvaluateAsync_CreditCardWithBalance_FiresInterestRule()
    {
        // Arrange
        var loader = new RulePackLoader();
        var pack = loader.Load(SampleRulePacks.CreditCard);
        var rulePacks = new Dictionary<string, RulePack> { ["credit_card_standard"] = pack };

        var engine = new IntegratedRuleEngine(rulePacks);

        var ctx = new RuleEvaluationContext(
            EvaluationDate: new DateOnly(2025, 6, 15),
            ObligationId: Guid.NewGuid(),
            InstallmentKey: null,
            CurrencyCode: "EGP",
            Facts: new Dictionary<string, object>
            {
                ["rule_pack_id"] = "credit_card_standard",
                ["outstanding.amount"] = 5000m
            }
        );

        // Act
        var (effects, trace) = await engine.EvaluateAsync(ctx, CancellationToken.None);

        // Assert
        Assert.Contains(effects, e => e.EffectType == RuleEffectTypes.AccrueInterest);

        var interestEffect = effects.First(e => e.EffectType == RuleEffectTypes.AccrueInterest);
        Assert.Equal(0.24m, Convert.ToDecimal(interestEffect.Data[RuleEffectFields.Rate]));
    }

    [Fact]
    public async Task EvaluateAsync_UnknownRulePack_ReturnsEmptyWithError()
    {
        // Arrange
        var engine = new IntegratedRuleEngine(new Dictionary<string, RulePack>());

        var ctx = new RuleEvaluationContext(
            EvaluationDate: new DateOnly(2025, 6, 15),
            ObligationId: Guid.NewGuid(),
            InstallmentKey: null,
            CurrencyCode: "EGP",
            Facts: new Dictionary<string, object>
            {
                ["rule_pack_id"] = "nonexistent_pack"
            }
        );

        // Act
        var (effects, trace) = await engine.EvaluateAsync(ctx, CancellationToken.None);

        // Assert
        Assert.Empty(effects);
        Assert.Empty(trace.FiredRuleKeys);
        Assert.Contains("error", trace.Debug.Keys);
    }

    [Fact]
    public async Task EvaluateAsync_WithResolver_LoadsFromExternalSource()
    {
        // Arrange
        var resolverCalled = false;
        var engine = new IntegratedRuleEngine(async (packId, asOf) =>
        {
            resolverCalled = true;
            await Task.CompletedTask;
            return SampleRulePacks.BasicLoan;
        });

        var ctx = new RuleEvaluationContext(
            EvaluationDate: new DateOnly(2025, 6, 15),
            ObligationId: Guid.NewGuid(),
            InstallmentKey: null,
            CurrencyCode: "EGP",
            Facts: new Dictionary<string, object>
            {
                ["rule_pack_id"] = "any_pack",
                ["installment.is_overdue"] = true
            }
        );

        // Act
        var (effects, _) = await engine.EvaluateAsync(ctx, CancellationToken.None);

        // Assert
        Assert.True(resolverCalled);
        Assert.NotEmpty(effects);
    }

    [Fact]
    public void RuleEngineFactory_CreateNoOp_ReturnsNoOpEngine()
    {
        // Act
        var engine = RuleEngineFactory.CreateNoOp();

        // Assert
        Assert.NotNull(engine);
        Assert.IsType<NoOpRuleEngine>(engine);
    }

    [Fact]
    public void RuleEngineFactory_CreateWithTracing_WrapsInnerEngine()
    {
        // Arrange
        var inner = RuleEngineFactory.CreateNoOp();

        // Act
        var tracing = RuleEngineFactory.CreateWithTracing(inner);

        // Assert
        Assert.NotNull(tracing);
        Assert.IsType<TracingRuleEngine>(tracing);
    }
}
