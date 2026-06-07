namespace DebtManager.Domain.Audit;

public sealed class AuditCollector
{
    private readonly List<AuditEntry> _entries = new();

    public void Add(AuditEntry entry) => _entries.Add(entry);

    public void Add(
        DateTimeOffset at,
        DateOnly effectiveDate,
        string category,
        string message,
        Guid? relatedEventId = null,
        Guid? obligationId = null,
        string? ruleKey = null,
        string? severity = null,
        IReadOnlyDictionary<string, string>? tags = null)
        => _entries.Add(new AuditEntry(at, effectiveDate, category, message, relatedEventId, obligationId, ruleKey, severity, tags));

    public IReadOnlyList<AuditEntry> Build()
        => _entries
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.At)
            .ToList()
            .AsReadOnly();
}
