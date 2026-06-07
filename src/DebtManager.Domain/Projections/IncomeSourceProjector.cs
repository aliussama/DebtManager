using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

/// <summary>
/// Projects EventEnvelopes into IncomeSourceState.
/// Deterministic: same inputs always produce same outputs.
/// Ordering: EffectiveDate ? OccurredAt ? EventId.
/// 
/// Also ingests IncomeRecorded events to accumulate totals on matching sources by name.
/// Does NOT auto-create sources from unmatched income strings.
/// </summary>
public static class IncomeSourceProjector
{
    public static IncomeSourceState Project(IEnumerable<EventEnvelope> envelopes)
    {
        var state = new IncomeSourceState();

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value)
            .ToList();

        foreach (var env in ordered)
        {
            Apply(state, env);
        }

        return state;
    }

    private static void Apply(IncomeSourceState state, EventEnvelope env)
    {
        var opt = DomainJson.Options;

        switch (env.EventType)
        {
            case nameof(IncomeSourceDefined):
            {
                var ev = JsonSerializer.Deserialize<IncomeSourceDefined>(env.PayloadJson, opt);
                if (ev == null) return;

                state.Sources[ev.SourceId] = new IncomeSourceRecord
                {
                    SourceId = ev.SourceId,
                    Name = ev.Name,
                    SourceType = ev.SourceType,
                    CurrencyCode = ev.CurrencyCode,
                    IsRecurring = ev.IsRecurring,
                    IsArchived = false,
                    TotalReceived = 0m,
                    LastReceivedDate = null,
                    CreatedEffectiveDate = ev.EffectiveDate,
                    Notes = ev.Notes
                };
                break;
            }

            case nameof(IncomeSourceArchived):
            {
                var ev = JsonSerializer.Deserialize<IncomeSourceArchived>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Sources.TryGetValue(ev.SourceId, out var source))
                {
                    source.IsArchived = true;
                    source.ArchiveReason = ev.Reason;
                }
                break;
            }

            case nameof(IncomeRecorded):
            {
                var ev = JsonSerializer.Deserialize<IncomeRecorded>(env.PayloadJson, opt);
                if (ev == null) return;

                var matched = state.FindByName(ev.Source);
                if (matched != null && !matched.IsArchived)
                {
                    matched.TotalReceived += ev.Amount.Amount;
                    var evDate = ev.EffectiveDate;
                    if (!matched.LastReceivedDate.HasValue || evDate > matched.LastReceivedDate.Value)
                        matched.LastReceivedDate = evDate;
                }
                break;
            }

            case nameof(SplitIncomeRecorded):
            {
                var ev = JsonSerializer.Deserialize<SplitIncomeRecorded>(env.PayloadJson, opt);
                if (ev == null) return;

                foreach (var line in ev.Lines)
                {
                    var matched = state.FindByName(line.Source);
                    if (matched != null && !matched.IsArchived)
                    {
                        matched.TotalReceived += line.Amount.Amount;
                        var evDate = ev.EffectiveDate;
                        if (!matched.LastReceivedDate.HasValue || evDate > matched.LastReceivedDate.Value)
                            matched.LastReceivedDate = evDate;
                    }
                }
                break;
            }
        }
    }
}
