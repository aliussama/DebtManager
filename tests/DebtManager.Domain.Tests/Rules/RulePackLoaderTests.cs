using DebtManager.Domain.Rules;
using DebtManager.Domain.Services.Rules;
using Xunit;

namespace DebtManager.Domain.Tests.Rules;

public class RulePackLoaderTests
{
    private readonly RulePackLoader _loader = new();

    [Fact]
    public void Load_BasicLoanPack_ParsesCorrectly()
    {
        // Act
        var pack = _loader.Load(SampleRulePacks.BasicLoan);

        // Assert
        Assert.Equal("basic_loan", pack.PackId);
        Assert.Equal("Basic Loan Rules", pack.DisplayName);
        Assert.Equal("EG", pack.CountryCode);
        Assert.Equal("EGP", pack.CurrencyCode);
        Assert.Single(pack.Versions);
    }

    [Fact]
    public void Load_CreditCardPack_ParsesInterestRule()
    {
        // Act
        var pack = _loader.Load(SampleRulePacks.CreditCard);

        // Assert
        var version = pack.Versions.First();
        Assert.Equal("2025-01", version.VersionLabel);
        Assert.Equal("active", version.Status);

        var interestRule = version.Rules.FirstOrDefault(r => r.RuleKey == "interest_accrual_24pct");
        Assert.NotNull(interestRule);

        var effect = interestRule.Then.First();
        Assert.Equal(RuleEffectTypes.AccrueInterest, effect.EffectType);
        Assert.Equal(0.24m, Convert.ToDecimal(effect.Data[RuleEffectFields.Rate]));
    }

    [Fact]
    public void Load_GraceRule_ParsesGraceDays()
    {
        // Act
        var pack = _loader.Load(SampleRulePacks.BasicLoan);

        // Assert
        var version = pack.Versions.First();
        var graceRule = version.Rules.FirstOrDefault(r => r.RuleKey == "grace_period_7_days");
        Assert.NotNull(graceRule);

        var effect = graceRule.Then.First();
        Assert.Equal(RuleEffectTypes.ApplyGrace, effect.EffectType);
        Assert.Equal(7, Convert.ToInt32(effect.Data[RuleEffectFields.GraceDays]));
    }

    [Fact]
    public void Load_PenaltyRule_ParsesAmount()
    {
        // Act
        var pack = _loader.Load(SampleRulePacks.BasicLoan);

        // Assert
        var version = pack.Versions.First();
        var penaltyRule = version.Rules.FirstOrDefault(r => r.RuleKey == "late_penalty_50egp");
        Assert.NotNull(penaltyRule);

        var effect = penaltyRule.Then.First();
        Assert.Equal(RuleEffectTypes.ApplyPenalty, effect.EffectType);
        Assert.Equal(50m, Convert.ToDecimal(effect.Data[RuleEffectFields.Amount]));
    }

    [Fact]
    public void Load_WhenClause_ParsesPredicates()
    {
        // Act
        var pack = _loader.Load(SampleRulePacks.BasicLoan);

        // Assert
        var version = pack.Versions.First();
        var penaltyRule = version.Rules.First(r => r.RuleKey == "late_penalty_50egp");

        Assert.Equal("and", penaltyRule.When.Op);
        Assert.Single(penaltyRule.When.Predicates);

        var predicate = penaltyRule.When.Predicates.First();
        Assert.Equal("installment.days_overdue", predicate.Fact);
        Assert.Equal("gt", predicate.Compare);
        Assert.Equal(7, Convert.ToInt32(predicate.Value));
    }

    [Fact]
    public void Load_MortgagePack_ParsesMultiplePredicates()
    {
        // Act
        var pack = _loader.Load(SampleRulePacks.Mortgage);

        // Assert
        var version = pack.Versions.First();
        var interestRule = version.Rules.First(r => r.RuleKey == "interest_accrual_variable");

        Assert.Equal(2, interestRule.When.Predicates.Count);
        Assert.Contains(interestRule.When.Predicates, p => p.Fact == "installment.days_overdue");
        Assert.Contains(interestRule.When.Predicates, p => p.Fact == "outstanding.amount");
    }

    [Fact]
    public void Load_PercentagePenalty_ParsesPenaltyType()
    {
        // Act
        var pack = _loader.Load(SampleRulePacks.CreditCard);

        // Assert
        var version = pack.Versions.First();
        var feeRule = version.Rules.First(r => r.RuleKey == "late_fee_percentage");

        var effect = feeRule.Then.First();
        Assert.Equal("percentage", effect.Data[RuleEffectFields.PenaltyType]?.ToString());
        Assert.Equal(500m, Convert.ToDecimal(effect.Data[RuleEffectFields.MaxPenalty]));
    }

    [Fact]
    public void Load_AllSamplePacks_ParseWithoutErrors()
    {
        // Act & Assert - no exceptions thrown
        foreach (var (packId, json) in SampleRulePacks.All)
        {
            var pack = _loader.Load(json);
            Assert.NotNull(pack);
            Assert.Equal(packId, pack.PackId);
            Assert.NotEmpty(pack.Versions);
        }
    }

    [Fact]
    public void LoadVersion_SingleVersion_ParsesCorrectly()
    {
        // Arrange
        const string versionJson = """
{
  "version": "1.0",
  "effective_from": "2025-06-01",
  "status": "active",
  "rules": [
    {
      "id": "test_rule",
      "when": { "all": [{ "field": "test", "op": "eq", "value": true }] },
      "effect": { "apply_penalty": { "amount": 100, "label": "Test" } }
    }
  ]
}
""";

        // Act
        var version = _loader.LoadVersion(versionJson);

        // Assert
        Assert.Equal("1.0", version.VersionLabel);
        Assert.Equal(new DateOnly(2025, 6, 1), version.EffectiveFrom);
        Assert.Single(version.Rules);
    }
}
