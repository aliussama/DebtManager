using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

/// <summary>
/// Projects EventEnvelopes into RetirementState.
/// Deterministic: same inputs always produce same outputs.
/// Ordering: EffectiveDate -> OccurredAt -> EventId.
/// </summary>
public static class RetirementProjector
{
    public static RetirementState Project(IEnumerable<EventEnvelope> envelopes, DateOnly? asOfDate = null)
    {
        var state = new RetirementState();

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

    private static void Apply(RetirementState state, EventEnvelope env)
    {
        var opt = DomainJson.Options;

        switch (env.EventType)
        {
            case nameof(RetirementProfileDefined):
            {
                var ev = JsonSerializer.Deserialize<RetirementProfileDefined>(env.PayloadJson, opt);
                if (ev == null) return;

                state.Profiles.Add(new RetirementProfileRecord
                {
                    ProfileId = ev.ProfileId,
                    ProfileName = ev.ProfileName,
                    RetirementDate = ev.RetirementDate,
                    DesiredMonthlySpending = ev.DesiredMonthlySpending,
                    LifeExpectancyYears = ev.LifeExpectancyYears,
                    WithdrawalStrategy = ev.WithdrawalStrategy,
                    SafeWithdrawalRate = ev.SafeWithdrawalRate,
                    DefinedDate = ev.EffectiveDate
                });
                break;
            }

            case nameof(RetirementAssumptionsSet):
            {
                var ev = JsonSerializer.Deserialize<RetirementAssumptionsSet>(env.PayloadJson, opt);
                if (ev == null) return;

                state.AllAssumptions.Add(new RetirementAssumptionsRecord
                {
                    AssumptionsId = ev.AssumptionsId,
                    Name = ev.Name,
                    ExpectedAnnualReturnRate = ev.ExpectedAnnualReturnRate,
                    ExpectedAnnualInflation = ev.ExpectedAnnualInflation,
                    ExpectedAnnualSalaryGrowth = ev.ExpectedAnnualSalaryGrowth,
                    CurrentMonthlySavings = ev.CurrentMonthlySavings,
                    ReportingCurrencyCode = ev.ReportingCurrencyCode,
                    DefinedDate = ev.EffectiveDate
                });
                break;
            }

            case nameof(RetirementAssumptionsArchived):
            {
                var ev = JsonSerializer.Deserialize<RetirementAssumptionsArchived>(env.PayloadJson, opt);
                if (ev == null) return;

                var record = state.AllAssumptions.FirstOrDefault(a => a.AssumptionsId == ev.AssumptionsId);
                if (record != null)
                    record.IsArchived = true;
                break;
            }
        }
    }
}
