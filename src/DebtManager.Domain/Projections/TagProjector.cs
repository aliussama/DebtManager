using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

/// <summary>
/// Deterministic projector for universal entity tags.
/// Ordering: EffectiveDate ? OccurredAt ? EventId.
/// </summary>
public static class TagProjector
{
    public static TagState Project(IEnumerable<EventEnvelope> envelopes)
    {
        var state = new TagState();
        var opt = DomainJson.Options;

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value);

        foreach (var env in ordered)
        {
            switch (env.EventType)
            {
                case nameof(EntityTagsReplaced):
                {
                    var ev = JsonSerializer.Deserialize<EntityTagsReplaced>(env.PayloadJson, opt);
                    if (ev == null) continue;

                    var key = (ev.EntityId, ev.EntityType);

                    // Remove previous usage counts for this entity
                    if (state.EntityTags.TryGetValue(key, out var previousTags))
                    {
                        foreach (var oldTag in previousTags)
                        {
                            var lowerOld = oldTag.ToLowerInvariant();
                            if (state.TagUsageCounts.TryGetValue(lowerOld, out var count))
                            {
                                count--;
                                if (count <= 0)
                                    state.TagUsageCounts.Remove(lowerOld);
                                else
                                    state.TagUsageCounts[lowerOld] = count;
                            }
                        }
                    }

                    // Insert new tags (case-insensitive uniqueness for the set, case preserved)
                    var newTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var tag in ev.Tags)
                    {
                        var trimmed = tag.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            newTags.Add(trimmed);
                    }

                    state.EntityTags[key] = newTags;

                    // Update usage counts
                    foreach (var tag in newTags)
                    {
                        var lowerNew = tag.ToLowerInvariant();
                        state.TagUsageCounts.TryGetValue(lowerNew, out var c);
                        state.TagUsageCounts[lowerNew] = c + 1;
                    }

                    break;
                }
            }
        }

        return state;
    }
}
