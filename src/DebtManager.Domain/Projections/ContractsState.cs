using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

public sealed class ContractRecord
{
    public Guid ContractId { get; set; }
    public Guid PartyId { get; set; }
    public string ContractType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string TermsJson { get; set; } = "{}";
    public bool IsArchived { get; set; }
}

public sealed class ContractsState
{
    public Dictionary<Guid, ContractRecord> Contracts { get; } = new();
}

public static class ContractsProjector
{
    public static ContractsState Project(IEnumerable<EventEnvelope> envelopes, DateOnly? asOfDate = null)
    {
        var state = new ContractsState();
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
                case nameof(ContractCreated):
                {
                    var ev = JsonSerializer.Deserialize<ContractCreated>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Contracts[ev.ContractId] = new ContractRecord
                    {
                        ContractId = ev.ContractId,
                        PartyId = ev.PartyId,
                        ContractType = ev.ContractType,
                        Title = ev.Title,
                        StartDate = ev.StartDate,
                        EndDate = ev.EndDate,
                        CurrencyCode = ev.CurrencyCode,
                        TermsJson = ev.TermsJson
                    };
                    break;
                }
                case nameof(ContractModified):
                {
                    var ev = JsonSerializer.Deserialize<ContractModified>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Contracts.TryGetValue(ev.ContractId, out var c))
                    {
                        c.Title = ev.Title;
                        c.EndDate = ev.EndDate;
                        c.TermsJson = ev.TermsJson;
                    }
                    break;
                }
                case nameof(ContractArchived):
                {
                    var ev = JsonSerializer.Deserialize<ContractArchived>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Contracts.TryGetValue(ev.ContractId, out var c))
                        c.IsArchived = true;
                    break;
                }
            }
        }

        return state;
    }
}
