using DebtManager.Domain.Rules;
using DebtManager.Domain.Services.Rules;
using DebtManager.Domain.ValueObjects;
using Xunit;

namespace DebtManager.Domain.Tests;

public class RuleEffectMapperInterestTests
{
    [Fact]
    public void InterestAccrualEffect_UsesFactsAndProducesInterestCharge()
    {
        var mapper = new RuleEffectMapper();

        var facts = new Dictionary<string, object>
        {
            { "installment_due_date", new DateOnly(2026, 1, 1) },
            { "outstanding_amount", 10000m },
            { "outstanding_currency", "EGP" }
        };

        var ctx = new RuleEvaluationContext(
            EvaluationDate: new DateOnly(2026, 2, 1),
            ObligationId: Guid.NewGuid(),
            InstallmentKey: Guid.NewGuid(),
            CurrencyCode: "EGP",
            Facts: facts
        );

        var effects = new[]
        {
            new RuleEffect(
                EffectType: RuleEffectTypes.InterestAccrual,
                Data: new Dictionary<string, object>
                {
                    { RuleEffectFields.Rate, 0.12m },
                    { RuleEffectFields.Compounding, "simple" },
                    { RuleEffectFields.Basis, "actual365" },
                    { RuleEffectFields.Label, "Accrued interest" },
                    { RuleEffectFields.RuleKey, "interest.v1" }
                }
            )
        };

        var trace = new RuleTrace(
            VersionId: new RulePackVersionId(Guid.NewGuid()),
            FiredRuleKeys: new List<string> { "interest.v1" },
            Debug: new Dictionary<string, object>()
        );

        var res = mapper.Map(ctx, effects, trace, Currency.EGP).ToList();

        Assert.Single(res);
        Assert.Equal(DebtManager.Domain.Projections.Charges.ChargeType.Interest, res[0].Type);
        Assert.True(res[0].Amount.Amount > 0m);
    }
}
