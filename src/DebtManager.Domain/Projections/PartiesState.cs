using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

public sealed class PartyRecord
{
    public Guid PartyId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DefaultCurrencyCode { get; set; } = string.Empty;
    public string? ContactJson { get; set; }
    public string[] Tags { get; set; } = [];
    public bool IsArchived { get; set; }
}

public sealed class PartiesState
{
    public Dictionary<Guid, PartyRecord> Parties { get; } = new();
}

public static class PartiesProjector
{
    public static PartiesState Project(IEnumerable<EventEnvelope> envelopes, DateOnly? asOfDate = null)
    {
        var state = new PartiesState();
        var opt = DomainJson.Options;

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value);

        foreach (var env in ordered)
        {
            if (asOfDate.HasValue && env.EffectiveDate > asOfDate.Value)
                continue;

            switch (env.EventType)
            {
                case nameof(PartyCreated):
                {
                    var ev = JsonSerializer.Deserialize<PartyCreated>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Parties[ev.PartyId] = new PartyRecord
                    {
                        PartyId = ev.PartyId,
                        Kind = ev.Kind,
                        Name = ev.Name,
                        DefaultCurrencyCode = ev.DefaultCurrencyCode,
                        ContactJson = ev.ContactJson,
                        Tags = ev.Tags ?? []
                    };
                    break;
                }
                case nameof(PartyModified):
                {
                    var ev = JsonSerializer.Deserialize<PartyModified>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Parties.TryGetValue(ev.PartyId, out var p))
                    {
                        p.Name = ev.Name;
                        p.DefaultCurrencyCode = ev.DefaultCurrencyCode;
                        p.ContactJson = ev.ContactJson;
                        p.Tags = ev.Tags ?? [];
                    }
                    break;
                }
                case nameof(PartyArchived):
                {
                    var ev = JsonSerializer.Deserialize<PartyArchived>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Parties.TryGetValue(ev.PartyId, out var p))
                        p.IsArchived = true;
                    break;
                }
            }
        }

        return state;
    }
}
