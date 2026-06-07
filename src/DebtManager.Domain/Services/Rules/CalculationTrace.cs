using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Services.Rules;

/// <summary>
/// Complete audit trail for any computed value.
/// Every number must answer: "Why is this number like this?"
/// This is the "accountant replacement" requirement.
/// </summary>
public sealed class CalculationTrace
{
    public Guid TraceId { get; }
    public DateTimeOffset ComputedAt { get; }
    public DateOnly EvaluationDate { get; }
    public Guid ObligationId { get; }
    public string? InstallmentKey { get; }

    /// <summary>Rules that were evaluated (in order).</summary>
    public IReadOnlyList<RuleEvaluationRecord> RulesEvaluated { get; }

    /// <summary>Rules that actually fired and produced effects.</summary>
    public IReadOnlyList<RuleFiredRecord> RulesFired { get; }

    /// <summary>Input facts used in evaluation.</summary>
    public IReadOnlyDictionary<string, object> InputFacts { get; }

    /// <summary>Computed effects with formula breakdowns.</summary>
    public IReadOnlyList<EffectBreakdown> Effects { get; }

    /// <summary>Total charges computed.</summary>
    public Money TotalCharges { get; }

    /// <summary>Human-readable summary.</summary>
    public string Summary { get; }

    internal CalculationTrace(
        Guid traceId,
        DateTimeOffset computedAt,
        DateOnly evaluationDate,
        Guid obligationId,
        string? installmentKey,
        IReadOnlyList<RuleEvaluationRecord> rulesEvaluated,
        IReadOnlyList<RuleFiredRecord> rulesFired,
        IReadOnlyDictionary<string, object> inputFacts,
        IReadOnlyList<EffectBreakdown> effects,
        Money totalCharges,
        string summary)
    {
        TraceId = traceId;
        ComputedAt = computedAt;
        EvaluationDate = evaluationDate;
        ObligationId = obligationId;
        InstallmentKey = installmentKey;
        RulesEvaluated = rulesEvaluated;
        RulesFired = rulesFired;
        InputFacts = inputFacts;
        Effects = effects;
        TotalCharges = totalCharges;
        Summary = summary;
    }

    public static CalculationTraceBuilder CreateBuilder(
        Guid obligationId,
        DateOnly evaluationDate,
        string? installmentKey = null)
    {
        return new CalculationTraceBuilder(obligationId, evaluationDate, installmentKey);
    }
}

/// <summary>
/// Record of a rule that was evaluated (whether it fired or not).
/// </summary>
public sealed record RuleEvaluationRecord(
    string RuleKey,
    string RulePackId,
    string Phase,
    int Priority,
    bool PredicateResult,
    IReadOnlyDictionary<string, PredicateEvaluation> PredicateDetails
);

/// <summary>
/// Details of a single predicate evaluation.
/// </summary>
public sealed record PredicateEvaluation(
    string Field,
    string Operator,
    object ExpectedValue,
    object? ActualValue,
    bool Result
);

/// <summary>
/// Record of a rule that fired and produced effects.
/// </summary>
public sealed record RuleFiredRecord(
    string RuleKey,
    string RulePackId,
    string Phase,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    IReadOnlyList<string> EffectTypes
);

/// <summary>
/// Detailed breakdown of a computed effect.
/// </summary>
public sealed record EffectBreakdown(
    string EffectType,
    string Label,
    Money Amount,
    string Formula,
    IReadOnlyDictionary<string, object> FormulaInputs,
    IReadOnlyList<string> IntermediateSteps,
    string RuleKey
);

/// <summary>
/// Builder for constructing calculation traces.
/// </summary>
public sealed class CalculationTraceBuilder
{
    private readonly Guid _traceId = Guid.NewGuid();
    private readonly DateTimeOffset _computedAt = DateTimeOffset.UtcNow;
    private readonly Guid _obligationId;
    private readonly DateOnly _evaluationDate;
    private readonly string? _installmentKey;

    private readonly List<RuleEvaluationRecord> _rulesEvaluated = new();
    private readonly List<RuleFiredRecord> _rulesFired = new();
    private readonly Dictionary<string, object> _inputFacts = new();
    private readonly List<EffectBreakdown> _effects = new();
    private Currency _currency = Currency.EGP;

    public CalculationTraceBuilder(Guid obligationId, DateOnly evaluationDate, string? installmentKey)
    {
        _obligationId = obligationId;
        _evaluationDate = evaluationDate;
        _installmentKey = installmentKey;
    }

    public CalculationTraceBuilder WithCurrency(Currency currency)
    {
        _currency = currency;
        return this;
    }

    public CalculationTraceBuilder AddInputFact(string key, object value)
    {
        _inputFacts[key] = value;
        return this;
    }

    public CalculationTraceBuilder AddInputFacts(IEnumerable<KeyValuePair<string, object>> facts)
    {
        foreach (var fact in facts)
        {
            _inputFacts[fact.Key] = fact.Value;
        }
        return this;
    }

    public CalculationTraceBuilder AddRuleEvaluation(
        string ruleKey,
        string rulePackId,
        string phase,
        int priority,
        bool predicateResult,
        IReadOnlyDictionary<string, PredicateEvaluation>? predicateDetails = null)
    {
        _rulesEvaluated.Add(new RuleEvaluationRecord(
            ruleKey,
            rulePackId,
            phase,
            priority,
            predicateResult,
            predicateDetails ?? new Dictionary<string, PredicateEvaluation>()
        ));

        return this;
    }

    public CalculationTraceBuilder AddRuleFired(
        string ruleKey,
        string rulePackId,
        string phase,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        IReadOnlyList<string> effectTypes)
    {
        _rulesFired.Add(new RuleFiredRecord(
            ruleKey,
            rulePackId,
            phase,
            effectiveFrom,
            effectiveTo,
            effectTypes
        ));

        return this;
    }

    public CalculationTraceBuilder AddEffect(
        string effectType,
        string label,
        Money amount,
        string formula,
        IReadOnlyDictionary<string, object>? formulaInputs = null,
        IReadOnlyList<string>? intermediateSteps = null,
        string? ruleKey = null)
    {
        _effects.Add(new EffectBreakdown(
            effectType,
            label,
            amount,
            formula,
            formulaInputs ?? new Dictionary<string, object>(),
            intermediateSteps ?? Array.Empty<string>(),
            ruleKey ?? "unknown"
        ));

        return this;
    }

    public CalculationTrace Build()
    {
        var totalCharges = _effects.Aggregate(
            Money.Zero(_currency),
            (acc, e) => acc.Add(e.Amount)
        );

        var summary = BuildSummary();

        return new CalculationTrace(
            _traceId,
            _computedAt,
            _evaluationDate,
            _obligationId,
            _installmentKey,
            _rulesEvaluated.AsReadOnly(),
            _rulesFired.AsReadOnly(),
            _inputFacts.AsReadOnly(),
            _effects.AsReadOnly(),
            totalCharges,
            summary
        );
    }

    private string BuildSummary()
    {
        var lines = new List<string>
        {
            $"Evaluation Date: {_evaluationDate:yyyy-MM-dd}",
            $"Rules Evaluated: {_rulesEvaluated.Count}",
            $"Rules Fired: {_rulesFired.Count}",
            $"Effects Produced: {_effects.Count}"
        };

        if (_rulesFired.Count > 0)
        {
            lines.Add("Fired Rules:");
            foreach (var rule in _rulesFired)
            {
                lines.Add($"  - {rule.RuleKey} ({rule.Phase})");
            }
        }

        if (_effects.Count > 0)
        {
            lines.Add("Effects:");
            foreach (var effect in _effects)
            {
                lines.Add($"  - {effect.Label}: {effect.Amount} [{effect.Formula}]");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}