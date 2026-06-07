using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Fx;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

/// <summary>
/// Projects CurrencySettings events into CurrencySettingsState.
/// Deterministic: EffectiveDate -> OccurredAt -> EventId.
/// </summary>
public static class CurrencySettingsProjector
{
    public static CurrencySettingsState Project(IEnumerable<EventEnvelope> envelopes)
    {
        var state = new CurrencySettingsState();

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value)
            .ToList();

        foreach (var env in ordered)
        {
            Apply(state, env);
        }

        // Resolve active profile: latest non-archived by EffectiveDate, OccurredAt, EventId
        var active = state.Profiles.Values
            .Where(p => !p.IsArchived)
            .OrderByDescending(p => p.EffectiveDate)
            .ThenByDescending(p => p.OccurredAt)
            .ThenByDescending(p => p.EventId)
            .FirstOrDefault();

        if (active != null)
        {
            state.ActiveProfileId = active.ProfileId;
            state.ReportingCurrencyCode = active.ReportingCurrencyCode;
            state.Policy = active.Policy;
            state.MaxAgeDays = active.MaxAgeDays;
            state.IsConfigured = true;
        }

        return state;
    }

    private static void Apply(CurrencySettingsState state, EventEnvelope env)
    {
        var opt = DomainJson.Options;

        switch (env.EventType)
        {
            case nameof(ReportingCurrencySet):
            {
                var ev = JsonSerializer.Deserialize<ReportingCurrencySet>(env.PayloadJson, opt);
                if (ev == null) return;

                if (!state.Profiles.TryGetValue(ev.ProfileId, out var profile))
                {
                    profile = new CurrencySettingsProfile { ProfileId = ev.ProfileId };
                    state.Profiles[ev.ProfileId] = profile;
                }

                profile.ReportingCurrencyCode = ev.ReportingCurrencyCode;
                profile.EffectiveDate = ev.EffectiveDate;
                profile.OccurredAt = env.OccurredAt;
                profile.EventId = env.EventId.Value;
                break;
            }

            case nameof(FxPolicySet):
            {
                var ev = JsonSerializer.Deserialize<FxPolicySet>(env.PayloadJson, opt);
                if (ev == null) return;

                if (!state.Profiles.TryGetValue(ev.ProfileId, out var profile))
                {
                    profile = new CurrencySettingsProfile { ProfileId = ev.ProfileId };
                    state.Profiles[ev.ProfileId] = profile;
                }

                profile.Policy = ev.Policy;
                profile.MaxAgeDays = ev.MaxAgeDays;
                profile.EffectiveDate = ev.EffectiveDate;
                profile.OccurredAt = env.OccurredAt;
                profile.EventId = env.EventId.Value;
                break;
            }

            case nameof(CurrencySettingsArchived):
            {
                var ev = JsonSerializer.Deserialize<CurrencySettingsArchived>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Profiles.TryGetValue(ev.ProfileId, out var profile))
                    profile.IsArchived = true;
                break;
            }
        }
    }
}
