namespace DebtManager.Domain.Projections;

/// <summary>
/// Projected state of all entity tags derived from events.
/// </summary>
public sealed class TagState
{
    /// <summary>
    /// Current tags for each (EntityId, EntityType) pair.
    /// </summary>
    public Dictionary<(Guid EntityId, string EntityType), HashSet<string>> EntityTags { get; } = new();

    /// <summary>
    /// Global tag usage counts (case-insensitive key = lowercase invariant).
    /// </summary>
    public Dictionary<string, int> TagUsageCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
}
