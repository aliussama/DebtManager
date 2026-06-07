namespace DebtManager.Domain.Rules;

public readonly record struct RulePackId(string Value);
public readonly record struct RulePackVersionId(Guid Value);

public sealed record RuleEvaluationContext(
    DateOnly EvaluationDate,
    Guid ObligationId,
    Guid? InstallmentKey,
    string CurrencyCode,
    IReadOnlyDictionary<string, object> Facts
);

public sealed record RuleEffect(string EffectType, IReadOnlyDictionary<string, object> Data);

public sealed record RuleTrace(
    RulePackVersionId VersionId,
    IReadOnlyList<string> FiredRuleKeys,
    IReadOnlyDictionary<string, object> Debug
);

public interface IRuleEngine
{
    Task<(IReadOnlyList<RuleEffect> Effects, RuleTrace Trace)> EvaluateAsync(
        RuleEvaluationContext ctx,
        CancellationToken ct);
}
