using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

/// <summary>
/// Projects EventEnvelopes into GoalsState.
/// Deterministic: same inputs always produce same outputs.
/// Ordering: EffectiveDate -> OccurredAt -> EventId.
/// </summary>
public static class GoalsProjector
{
    public static GoalsState Project(IEnumerable<EventEnvelope> envelopes, DateOnly? asOfDate = null)
    {
        var state = new GoalsState();

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

    private static void Apply(GoalsState state, EventEnvelope env)
    {
        var opt = DomainJson.Options;

        switch (env.EventType)
        {
            case nameof(FinancialGoalCreated):
            {
                var ev = JsonSerializer.Deserialize<FinancialGoalCreated>(env.PayloadJson, opt);
                if (ev == null) return;

                state.Goals[ev.GoalId] = new FinancialGoalRecord
                {
                    GoalId = ev.GoalId,
                    Name = ev.Name,
                    GoalType = ev.GoalType,
                    TargetAmount = ev.TargetAmount,
                    TargetDate = ev.TargetDate,
                    Notes = ev.Notes,
                    Tags = ev.Tags ?? [],
                    CreatedDate = ev.EffectiveDate
                };
                state.ContributionsByGoal[ev.GoalId] = new List<GoalContributionRecord>();
                break;
            }

            case nameof(FinancialGoalModified):
            {
                var ev = JsonSerializer.Deserialize<FinancialGoalModified>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Goals.TryGetValue(ev.GoalId, out var goal))
                {
                    goal.Name = ev.Name;
                    goal.GoalType = ev.GoalType;
                    goal.TargetAmount = ev.TargetAmount;
                    goal.TargetDate = ev.TargetDate;
                    goal.Notes = ev.Notes;
                    goal.Tags = ev.Tags ?? [];
                }
                break;
            }

            case nameof(FinancialGoalArchived):
            {
                var ev = JsonSerializer.Deserialize<FinancialGoalArchived>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Goals.TryGetValue(ev.GoalId, out var goal))
                    goal.IsArchived = true;
                break;
            }

            case nameof(GoalContributionRecorded):
            {
                var ev = JsonSerializer.Deserialize<GoalContributionRecorded>(env.PayloadJson, opt);
                if (ev == null) return;

                if (!state.ContributionsByGoal.ContainsKey(ev.GoalId))
                    state.ContributionsByGoal[ev.GoalId] = new List<GoalContributionRecord>();

                state.ContributionsByGoal[ev.GoalId].Add(new GoalContributionRecord
                {
                    ContributionId = ev.ContributionId,
                    GoalId = ev.GoalId,
                    AccountId = ev.AccountId,
                    Amount = ev.Amount,
                    EffectiveDate = ev.EffectiveDate,
                    Reference = ev.Reference
                });
                break;
            }

            case nameof(GoalContributionReversed):
            {
                var ev = JsonSerializer.Deserialize<GoalContributionReversed>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.ContributionsByGoal.TryGetValue(ev.GoalId, out var list))
                {
                    var contrib = list.FirstOrDefault(c => c.ContributionId == ev.ContributionId);
                    if (contrib != null)
                        contrib.IsReversed = true;
                }
                break;
            }
        }
    }
}
