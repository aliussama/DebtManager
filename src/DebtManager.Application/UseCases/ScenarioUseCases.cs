using System.Text.Json;
using System.Text.RegularExpressions;
using DebtManager.Domain.Events;
using DebtManager.Domain.Forecasting;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- DTOs ---

public sealed record ScenarioListItemDto(
    Guid ScenarioId,
    string Name,
    string Notes,
    bool IsArchived,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    string Granularity,
    int ChangeCount
);

public sealed record ScenarioDetailDto(
    Guid ScenarioId,
    string Name,
    string Notes,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    string Granularity,
    IReadOnlyList<ScenarioChangeDto> Changes
);

public sealed record ScenarioChangeDto(
    Guid ChangeId,
    string Kind,
    string PayloadJson,
    bool IsRemoved
);

// --- Commands ---

public sealed record CreateForecastScenarioCommand(
    Guid? ScenarioId,
    string Name,
    string Notes,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    ForecastGranularity Granularity
);

public sealed record ModifyForecastScenarioCommand(Guid ScenarioId, string Name, string Notes);

public sealed record ArchiveForecastScenarioCommand(Guid ScenarioId, string Reason);

public sealed record AddScenarioChangeCommand(
    Guid ScenarioId,
    Guid? ChangeId,
    ScenarioChangeKind Kind,
    string PayloadJson
);

public sealed record RemoveScenarioChangeCommand(Guid ScenarioId, Guid ChangeId, string Reason);

// --- Handlers ---

public sealed class CreateForecastScenarioHandler
{
    private readonly IEventStore _store;
    public CreateForecastScenarioHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(CreateForecastScenarioCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Name))
            throw new InvalidOperationException("Scenario name is required.");
        if (cmd.HorizonEnd <= cmd.HorizonStart)
            throw new InvalidOperationException("Horizon end must be after start.");

        var id = cmd.ScenarioId ?? Guid.NewGuid();
        var ev = new ForecastScenarioCreated(id, cmd.Name, cmd.Notes ?? string.Empty,
            cmd.HorizonStart, cmd.HorizonEnd, cmd.Granularity.ToString(), cmd.HorizonStart);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(id),
            nameof(ForecastScenarioCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return id;
    }
}

public sealed class ModifyForecastScenarioHandler
{
    private readonly IEventStore _store;
    public ModifyForecastScenarioHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ModifyForecastScenarioCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new ForecastScenarioModified(cmd.ScenarioId, cmd.Name, cmd.Notes, DateOnly.FromDateTime(DateTime.Today));
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.ScenarioId),
            nameof(ForecastScenarioModified), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class ArchiveForecastScenarioHandler
{
    private readonly IEventStore _store;
    public ArchiveForecastScenarioHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveForecastScenarioCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new ForecastScenarioArchived(cmd.ScenarioId, DateOnly.FromDateTime(DateTime.Today), cmd.Reason);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.ScenarioId),
            nameof(ForecastScenarioArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class AddScenarioChangeHandler
{
    private readonly IEventStore _store;
    public AddScenarioChangeHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(AddScenarioChangeCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        // Validate scenario exists
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = ScenarioProjector.Project(envelopes);
        if (!state.Scenarios.ContainsKey(cmd.ScenarioId))
            throw new InvalidOperationException($"Scenario {cmd.ScenarioId} not found.");

        var changeId = cmd.ChangeId ?? Guid.NewGuid();
        var ev = new ForecastScenarioChangeAdded(cmd.ScenarioId, changeId, cmd.Kind.ToString(), cmd.PayloadJson,
            DateOnly.FromDateTime(DateTime.Today));
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.ScenarioId),
            nameof(ForecastScenarioChangeAdded), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return changeId;
    }
}

public sealed class RemoveScenarioChangeHandler
{
    private readonly IEventStore _store;
    public RemoveScenarioChangeHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(RemoveScenarioChangeCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        // Idempotent: if already removed, just return
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = ScenarioProjector.Project(envelopes);

        if (!state.Scenarios.TryGetValue(cmd.ScenarioId, out var scenario))
            throw new InvalidOperationException($"Scenario {cmd.ScenarioId} not found.");

        if (scenario.Changes.TryGetValue(cmd.ChangeId, out var change) && change.IsRemoved)
            return; // Idempotent

        var ev = new ForecastScenarioChangeRemoved(cmd.ScenarioId, cmd.ChangeId,
            DateOnly.FromDateTime(DateTime.Today), cmd.Reason);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.ScenarioId),
            nameof(ForecastScenarioChangeRemoved), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class GetScenarioListHandler
{
    private readonly IEventStore _store;
    public GetScenarioListHandler(IEventStore store) => _store = store;

    public async Task<IReadOnlyList<ScenarioListItemDto>> HandleAsync(CancellationToken ct)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = ScenarioProjector.Project(envelopes);

        return state.Scenarios.Values
            .Select(s => new ScenarioListItemDto(
                s.ScenarioId, s.Name, s.Notes, s.IsArchived,
                s.HorizonStart, s.HorizonEnd, s.Granularity.ToString(),
                s.Changes.Values.Count(c => !c.IsRemoved)))
            .OrderBy(s => s.IsArchived)
            .ThenByDescending(s => s.HorizonStart)
            .ToList();
    }
}

public sealed class GetScenarioDetailHandler
{
    private readonly IEventStore _store;
    public GetScenarioDetailHandler(IEventStore store) => _store = store;

    public async Task<ScenarioDetailDto?> HandleAsync(Guid scenarioId, CancellationToken ct)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = ScenarioProjector.Project(envelopes);

        if (!state.Scenarios.TryGetValue(scenarioId, out var s))
            return null;

        var changes = s.Changes.Values
            .Select(c => new ScenarioChangeDto(c.ChangeId, c.Kind.ToString(), c.PayloadJson, c.IsRemoved))
            .ToList();

        return new ScenarioDetailDto(s.ScenarioId, s.Name, s.Notes,
            s.HorizonStart, s.HorizonEnd, s.Granularity.ToString(), changes);
    }
}
