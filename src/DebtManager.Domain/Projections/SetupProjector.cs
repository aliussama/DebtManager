using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

/// <summary>
/// Projects EventEnvelopes into SetupState.
/// Deterministic: same inputs always produce same outputs.
/// Ordering: EffectiveDate -> OccurredAt -> EventId.
/// </summary>
public static class SetupProjector
{
    public static SetupState Project(IEnumerable<EventEnvelope> envelopes, DateOnly? asOfDate = null)
    {
        var state = new SetupState();

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value)
            .ToList();

        foreach (var env in ordered)
        {
            if (asOfDate.HasValue && env.EffectiveDate > asOfDate.Value)
                continue;

            Apply(state, env);
        }

        return state;
    }

    private static void Apply(SetupState state, EventEnvelope env)
    {
        var opt = DomainJson.Options;

        switch (env.EventType)
        {
            case nameof(InitialSetupCompleted):
            {
                var ev = JsonSerializer.Deserialize<InitialSetupCompleted>(env.PayloadJson, opt);
                if (ev == null) return;

                state.IsInitialSetupCompleted = true;
                state.CompletedOn = ev.EffectiveDate;
                state.SetupId = ev.SetupId;
                state.ReportingCurrencyCode = ev.ReportingCurrencyCode;
                state.FiscalYearStartMonth = ev.FiscalYearStartMonth;
                state.CreatedDefaultAccounts = ev.CreatedDefaultAccounts;
                state.CreatedDefaultCategories = ev.CreatedDefaultCategories;
                state.SeededDemoData = ev.SeededDemoData;
                break;
            }

            case nameof(DemoDataSeeded):
            {
                var ev = JsonSerializer.Deserialize<DemoDataSeeded>(env.PayloadJson, opt);
                if (ev == null) return;

                state.IsDemoModeActive = true;
                state.DemoSeedId = ev.DemoSeedId;
                state.SeededDemoData = true;
                break;
            }

            case nameof(DemoDataCleared):
            {
                var ev = JsonSerializer.Deserialize<DemoDataCleared>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.DemoSeedId == ev.DemoSeedId)
                {
                    state.IsDemoModeActive = false;
                    state.DemoSeedId = null;
                }
                break;
            }

            case nameof(DefaultAccountsCreated):
            {
                var ev = JsonSerializer.Deserialize<DefaultAccountsCreated>(env.PayloadJson, opt);
                if (ev == null) return;

                state.CreatedDefaultAccounts = true;
                break;
            }

            case nameof(DefaultCategoriesCreated):
            {
                var ev = JsonSerializer.Deserialize<DefaultCategoriesCreated>(env.PayloadJson, opt);
                if (ev == null) return;

                state.CreatedDefaultCategories = true;
                break;
            }
        }
    }
}
