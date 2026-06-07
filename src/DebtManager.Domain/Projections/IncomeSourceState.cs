using DebtManager.Domain.Events;

namespace DebtManager.Domain.Projections;

/// <summary>
/// A single income source record derived from events.
/// </summary>
public sealed class IncomeSourceRecord
{
    public Guid SourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public IncomeSourceType SourceType { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public bool IsRecurring { get; set; }
    public bool IsArchived { get; set; }
    public string? ArchiveReason { get; set; }
    public decimal TotalReceived { get; set; }
    public DateOnly? LastReceivedDate { get; set; }
    public DateOnly CreatedEffectiveDate { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Summary of income source state.
/// </summary>
public sealed record IncomeSourceSummary(
    int ActiveCount,
    int ArchivedCount,
    decimal TotalReceivedAllSources
);

/// <summary>
/// Full income source state derived deterministically from events.
/// </summary>
public sealed class IncomeSourceState
{
    public Dictionary<Guid, IncomeSourceRecord> Sources { get; } = new();

    public IReadOnlyList<IncomeSourceRecord> GetActiveSources()
        => Sources.Values.Where(s => !s.IsArchived).OrderBy(s => s.Name).ToList();

    public IncomeSourceRecord? TryGet(Guid sourceId)
        => Sources.TryGetValue(sourceId, out var s) ? s : null;

    public IncomeSourceRecord? FindByName(string name)
    {
        var key = NormalizeNameKey(name);
        return Sources.Values.FirstOrDefault(s => NormalizeNameKey(s.Name) == key);
    }

    public static string NormalizeNameKey(string name)
        => name.Trim().ToUpperInvariant();

    public IncomeSourceSummary GetSummary()
    {
        var active = Sources.Values.Count(s => !s.IsArchived);
        var archived = Sources.Values.Count(s => s.IsArchived);
        var total = Sources.Values.Sum(s => s.TotalReceived);
        return new IncomeSourceSummary(active, archived, total);
    }
}
