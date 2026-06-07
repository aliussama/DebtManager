using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Commands ---

public sealed record CreatePartyCommand(
    Guid? PartyId, string Kind, string Name,
    string DefaultCurrencyCode, string? ContactJson, string[] Tags,
    DateOnly EffectiveDate);

public sealed record ModifyPartyCommand(
    Guid PartyId, string Name,
    string DefaultCurrencyCode, string? ContactJson, string[] Tags,
    DateOnly EffectiveDate);

public sealed record ArchivePartyCommand(Guid PartyId, string Reason, DateOnly EffectiveDate);

// --- DTOs ---

public sealed record PartyListItemDto(
    Guid PartyId, string Kind, string Name,
    string DefaultCurrencyCode, string? ContactJson, string[] Tags, bool IsArchived);

// --- Handlers ---

public sealed class CreatePartyHandler
{
    private readonly IEventStore _store;
    public CreatePartyHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(CreatePartyCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var id = cmd.PartyId ?? Guid.NewGuid();
        var ev = new PartyCreated(id, cmd.Kind, cmd.Name, cmd.DefaultCurrencyCode,
            cmd.ContactJson, cmd.Tags ?? [], cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(id),
            nameof(PartyCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
        return id;
    }
}

public sealed class ModifyPartyHandler
{
    private readonly IEventStore _store;
    public ModifyPartyHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ModifyPartyCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new PartyModified(cmd.PartyId, cmd.Name, cmd.DefaultCurrencyCode,
            cmd.ContactJson, cmd.Tags ?? [], cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.PartyId),
            nameof(PartyModified), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class ArchivePartyHandler
{
    private readonly IEventStore _store;
    public ArchivePartyHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchivePartyCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new PartyArchived(cmd.PartyId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.PartyId),
            nameof(PartyArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class GetPartiesListHandler
{
    private readonly IEventStore _store;
    public GetPartiesListHandler(IEventStore store) => _store = store;

    public async Task<IReadOnlyList<PartyListItemDto>> HandleAsync(bool includeArchived, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = PartiesProjector.Project(all);
        return state.Parties.Values
            .Where(p => includeArchived || !p.IsArchived)
            .Select(p => new PartyListItemDto(p.PartyId, p.Kind, p.Name,
                p.DefaultCurrencyCode, p.ContactJson, p.Tags, p.IsArchived))
            .ToList();
    }
}
