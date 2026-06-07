namespace DebtManager.Domain.Audit;

public sealed record AuditEntry(
    DateTimeOffset At,
    DateOnly EffectiveDate,
    string Category,
    string Message,
    Guid? RelatedEventId = null,
    Guid? ObligationId = null,
    string? RuleKey = null,
    string? Severity = null,
    IReadOnlyDictionary<string, string>? Tags = null
);
