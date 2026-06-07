using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Commands ---

public sealed record CreateCategoryCommand(Guid? CategoryId, string Name, string Kind, Guid? ParentCategoryId);
public sealed record RenameCategoryCommand(Guid CategoryId, string NewName);
public sealed record ArchiveCategoryCommand(Guid CategoryId, string Reason);

// --- DTOs ---

public sealed record CategoryListItemDto(Guid CategoryId, string Name, string Kind, Guid? ParentCategoryId, bool IsArchived);

// --- Handlers ---

public sealed class CreateCategoryHandler
{
    private readonly IEventStore _store;
    public CreateCategoryHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(CreateCategoryCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var id = cmd.CategoryId ?? Guid.NewGuid();
        var ev = new CategoryCreated(id, cmd.Name, cmd.Kind, cmd.ParentCategoryId, DateOnly.FromDateTime(DateTime.Today));
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(id),
            nameof(CategoryCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return id;
    }
}

public sealed class RenameCategoryHandler
{
    private readonly IEventStore _store;
    public RenameCategoryHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(RenameCategoryCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new CategoryRenamed(cmd.CategoryId, cmd.NewName, DateOnly.FromDateTime(DateTime.Today));
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.CategoryId),
            nameof(CategoryRenamed), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class ArchiveCategoryHandler
{
    private readonly IEventStore _store;
    public ArchiveCategoryHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveCategoryCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new CategoryArchived(cmd.CategoryId, DateOnly.FromDateTime(DateTime.Today), cmd.Reason);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.CategoryId),
            nameof(CategoryArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class GetCategoriesListHandler
{
    private readonly IEventStore _store;
    public GetCategoriesListHandler(IEventStore store) => _store = store;

    public async Task<IReadOnlyList<CategoryListItemDto>> HandleAsync(CancellationToken ct)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = CategoryProjector.Project(envelopes);
        return state.Categories.Values
            .OrderBy(c => c.IsArchived).ThenBy(c => c.Kind).ThenBy(c => c.Name)
            .Select(c => new CategoryListItemDto(c.CategoryId, c.Name, c.Kind, c.ParentCategoryId, c.IsArchived))
            .ToList();
    }
}
