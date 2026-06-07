using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

/// <summary>
/// Projects EventEnvelopes into TaxState.
/// Deterministic: same inputs always produce same outputs.
/// Ordering: EffectiveDate -> OccurredAt -> EventId.
/// </summary>
public static class TaxProjector
{
    public static TaxState Project(IEnumerable<EventEnvelope> envelopes, DateOnly? asOfDate = null)
    {
        var state = new TaxState();

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

    private static void Apply(TaxState state, EventEnvelope env)
    {
        var opt = DomainJson.Options;

        switch (env.EventType)
        {
            case nameof(TaxProfileCreated):
            {
                var ev = JsonSerializer.Deserialize<TaxProfileCreated>(env.PayloadJson, opt);
                if (ev == null) return;

                state.Profiles[ev.ProfileId] = new TaxProfileRecord
                {
                    ProfileId = ev.ProfileId,
                    Name = ev.Name,
                    CountryCode = ev.CountryCode,
                    TaxYearStartMonth = ev.TaxYearStartMonth,
                    TaxYearStartDay = ev.TaxYearStartDay,
                    BaseCurrencyCode = ev.BaseCurrencyCode,
                    CreatedDate = ev.EffectiveDate
                };
                break;
            }

            case nameof(TaxProfileModified):
            {
                var ev = JsonSerializer.Deserialize<TaxProfileModified>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Profiles.TryGetValue(ev.ProfileId, out var profile))
                {
                    if (ev.Name != null) profile.Name = ev.Name;
                    if (ev.CountryCode != null) profile.CountryCode = ev.CountryCode;
                    if (ev.TaxYearStartMonth.HasValue) profile.TaxYearStartMonth = ev.TaxYearStartMonth.Value;
                    if (ev.TaxYearStartDay.HasValue) profile.TaxYearStartDay = ev.TaxYearStartDay.Value;
                    if (ev.BaseCurrencyCode != null) profile.BaseCurrencyCode = ev.BaseCurrencyCode;
                }
                break;
            }

            case nameof(TaxProfileArchived):
            {
                var ev = JsonSerializer.Deserialize<TaxProfileArchived>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Profiles.TryGetValue(ev.ProfileId, out var profile))
                    profile.IsArchived = true;
                break;
            }

            case nameof(TaxRuleDefined):
            {
                var ev = JsonSerializer.Deserialize<TaxRuleDefined>(env.PayloadJson, opt);
                if (ev == null) return;

                state.AllRules.Add(new TaxRuleRecord
                {
                    RuleId = ev.RuleId,
                    AppliesTo = ev.AppliesTo,
                    MatchValue = ev.MatchValue,
                    TaxCategory = ev.TaxCategory
                });
                break;
            }

            case nameof(TaxRuleArchived):
            {
                var ev = JsonSerializer.Deserialize<TaxRuleArchived>(env.PayloadJson, opt);
                if (ev == null) return;

                var rule = state.AllRules.FirstOrDefault(r => r.RuleId == ev.RuleId);
                if (rule != null)
                    rule.IsArchived = true;
                break;
            }

            case nameof(TaxConfirmClassification):
            {
                var ev = JsonSerializer.Deserialize<TaxConfirmClassification>(env.PayloadJson, opt);
                if (ev == null) return;

                state.ConfirmedClassifications[(ev.SourceType, ev.SourceId)] = ev.TaxCategory;
                break;
            }
        }
    }
}
