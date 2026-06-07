using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Forecasting;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

/// <summary>
/// Deterministic projection of scenario events into ScenarioState.
/// Ordering: EffectiveDate -> OccurredAt -> EventId.
/// </summary>
public static class ScenarioProjector
{
    public static ScenarioState Project(IEnumerable<EventEnvelope> envelopes)
    {
        var state = new ScenarioState();

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value);

        foreach (var env in ordered)
            Apply(state, env);

        return state;
    }

    private static void Apply(ScenarioState state, EventEnvelope env)
    {
        var opt = DomainJson.Options;

        switch (env.EventType)
        {
            case nameof(ForecastScenarioCreated):
            {
                var ev = JsonSerializer.Deserialize<ForecastScenarioCreated>(env.PayloadJson, opt);
                if (ev == null) return;

                state.Scenarios[ev.ScenarioId] = new ScenarioRecord
                {
                    ScenarioId = ev.ScenarioId,
                    Name = ev.Name,
                    Notes = ev.Notes,
                    HorizonStart = ev.HorizonStart,
                    HorizonEnd = ev.HorizonEnd,
                    Granularity = Enum.TryParse<ForecastGranularity>(ev.Granularity, out var g) ? g : ForecastGranularity.Monthly
                };
                break;
            }

            case nameof(ForecastScenarioModified):
            {
                var ev = JsonSerializer.Deserialize<ForecastScenarioModified>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Scenarios.TryGetValue(ev.ScenarioId, out var s))
                {
                    s.Name = ev.Name;
                    s.Notes = ev.Notes;
                }
                break;
            }

            case nameof(ForecastScenarioArchived):
            {
                var ev = JsonSerializer.Deserialize<ForecastScenarioArchived>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Scenarios.TryGetValue(ev.ScenarioId, out var s))
                    s.IsArchived = true;
                break;
            }

            case nameof(ForecastScenarioChangeAdded):
            {
                var ev = JsonSerializer.Deserialize<ForecastScenarioChangeAdded>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Scenarios.TryGetValue(ev.ScenarioId, out var s))
                {
                    s.Changes[ev.ChangeId] = new ScenarioChangeRecord
                    {
                        ChangeId = ev.ChangeId,
                        Kind = Enum.TryParse<ScenarioChangeKind>(ev.ChangeKind, out var k) ? k : ScenarioChangeKind.OneTimeExpense,
                        PayloadJson = ev.PayloadJson,
                        IsRemoved = false
                    };
                }
                break;
            }

            case nameof(ForecastScenarioChangeRemoved):
            {
                var ev = JsonSerializer.Deserialize<ForecastScenarioChangeRemoved>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Scenarios.TryGetValue(ev.ScenarioId, out var s) &&
                    s.Changes.TryGetValue(ev.ChangeId, out var c))
                {
                    c.IsRemoved = true;
                }
                break;
            }
        }
    }
}
