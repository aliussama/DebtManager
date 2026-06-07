using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Rules;
using DebtManager.Domain.Services.Rules;

namespace DebtManager.Application.Services;

/// <summary>
/// Factory for creating application services with proper dependencies.
/// </summary>
public static class ApplicationServiceFactory
{
    /// <summary>
    /// Create the obligation management service with in-memory components (for testing).
    /// </summary>
    public static ObligationManagementService CreateInMemory(
        IEventStore eventStore,
        IReadOnlyDictionary<string, RulePack>? rulePacks = null)
    {
        var ruleEngine = rulePacks != null
            ? new IntegratedRuleEngine(rulePacks)
            : (IRuleEngine)new NoOpRuleEngine();

        return new ObligationManagementService(eventStore, ruleEngine);
    }

    /// <summary>
    /// Create the obligation management service with JSON-based rule packs.
    /// </summary>
    public static ObligationManagementService CreateWithJsonRules(
        IEventStore eventStore,
        Func<string, DateOnly, Task<string?>> rulePackResolver)
    {
        var ruleEngine = new IntegratedRuleEngine(rulePackResolver);
        return new ObligationManagementService(eventStore, ruleEngine);
    }

    /// <summary>
    /// Create the obligation management service with sample rule packs.
    /// </summary>
    public static ObligationManagementService CreateWithSampleRules(IEventStore eventStore)
    {
        var loader = new RulePackLoader();
        var rulePacks = new Dictionary<string, RulePack>();

        foreach (var (packId, json) in SampleRulePacks.All)
        {
            rulePacks[packId] = loader.Load(json);
        }

        return CreateInMemory(eventStore, rulePacks);
    }

    /// <summary>
    /// Create a tracing-enabled rule engine.
    /// </summary>
    public static TracingRuleEngine CreateTracingRuleEngine(IRuleEngine inner)
    {
        return new TracingRuleEngine(inner);
    }

    /// <summary>
    /// Create a no-op rule engine (for testing without rules).
    /// </summary>
    public static IRuleEngine CreateNoOpRuleEngine()
    {
        return new NoOpRuleEngine();
    }

    /// <summary>
    /// Create an integrated rule engine with static rule packs.
    /// </summary>
    public static IRuleEngine CreateIntegratedRuleEngine(IReadOnlyDictionary<string, RulePack> rulePacks)
    {
        return new IntegratedRuleEngine(rulePacks);
    }
}
