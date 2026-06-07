using System.Text.Json;
using DebtManager.Domain.Ai;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

public static class AiAdvisorProjector
{
    public static AiAdvisorState Project(IEnumerable<EventEnvelope> envelopes)
    {
        var state = new AiAdvisorState();
        var opt = DomainJson.Options;

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value);

        foreach (var env in ordered)
        {
            switch (env.EventType)
            {
                case nameof(AiInsightRecorded):
                {
                    var ev = JsonSerializer.Deserialize<AiInsightRecorded>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Insights[ev.InsightId] = new AiInsight
                    {
                        InsightId = ev.InsightId,
                        InsightCode = ev.InsightCode,
                        Severity = ev.Severity,
                        Area = ev.Area,
                        Title = ev.Title,
                        Message = ev.Message,
                        RecordedDate = ev.EffectiveDate
                    };
                    break;
                }
                case nameof(AiProposalCreated):
                {
                    var ev = JsonSerializer.Deserialize<AiProposalCreated>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Proposals[ev.ProposalId] = new AiProposal
                    {
                        ProposalId = ev.ProposalId,
                        ProposalKind = ev.ProposalKind,
                        ProposalJson = ev.ProposalJson,
                        Reason = ev.Reason,
                        RiskLevel = ev.RiskLevel,
                        Status = AiProposalStatus.Pending,
                        CreatedDate = ev.EffectiveDate
                    };
                    break;
                }
                case nameof(AiProposalApproved):
                {
                    var ev = JsonSerializer.Deserialize<AiProposalApproved>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Proposals.TryGetValue(ev.ProposalId, out var p))
                        p.Status = AiProposalStatus.Approved;
                    break;
                }
                case nameof(AiProposalRejected):
                {
                    var ev = JsonSerializer.Deserialize<AiProposalRejected>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Proposals.TryGetValue(ev.ProposalId, out var p))
                    {
                        p.Status = AiProposalStatus.Rejected;
                        p.RejectionReason = ev.Reason;
                    }
                    break;
                }
                case nameof(AiSettingsUpdated):
                {
                    var ev = JsonSerializer.Deserialize<AiSettingsUpdated>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Settings = new AiSettingsRecord
                    {
                        Enabled = ev.Enabled,
                        AllowInternetAccess = ev.AllowInternetAccess,
                        AllowAutoProposalGeneration = ev.AllowAutoProposalGeneration
                    };
                    break;
                }
            }
        }

        return state;
    }
}
