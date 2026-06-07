using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

/// <summary>
/// State of a single category.
/// </summary>
public sealed class CategoryItem
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty; // "income" or "expense"
    public Guid? ParentCategoryId { get; set; }
    public bool IsArchived { get; set; }
}

/// <summary>
/// Full category state derived from events.
/// </summary>
public sealed class CategoryState
{
    public Dictionary<Guid, CategoryItem> Categories { get; } = new();
}

/// <summary>
/// Projects category events into CategoryState.
/// </summary>
public static class CategoryProjector
{
    public static CategoryState Project(IEnumerable<EventEnvelope> envelopes)
    {
        var state = new CategoryState();
        var opt = DomainJson.Options;

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value);

        foreach (var env in ordered)
        {
            switch (env.EventType)
            {
                case nameof(CategoryCreated):
                {
                    var ev = JsonSerializer.Deserialize<CategoryCreated>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Categories[ev.CategoryId] = new CategoryItem
                    {
                        CategoryId = ev.CategoryId,
                        Name = ev.Name,
                        Kind = ev.Kind,
                        ParentCategoryId = ev.ParentCategoryId
                    };
                    break;
                }
                case nameof(CategoryRenamed):
                {
                    var ev = JsonSerializer.Deserialize<CategoryRenamed>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Categories.TryGetValue(ev.CategoryId, out var cat))
                        cat.Name = ev.NewName;
                    break;
                }
                case nameof(CategoryArchived):
                {
                    var ev = JsonSerializer.Deserialize<CategoryArchived>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Categories.TryGetValue(ev.CategoryId, out var cat))
                        cat.IsArchived = true;
                    break;
                }
            }
        }

        return state;
    }
}
