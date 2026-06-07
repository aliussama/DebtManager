using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

/// <summary>
/// Projects data quality events into DataQualityState.
/// </summary>
public static class DataQualityProjector
{
    public static DataQualityState Project(IEnumerable<EventEnvelope> envelopes)
    {
        var state = new DataQualityState();
        var opt = DomainJson.Options;

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value);

        foreach (var env in ordered)
        {
            switch (env.EventType)
            {
                case nameof(DataQualityScanRecorded):
                {
                    var ev = JsonSerializer.Deserialize<DataQualityScanRecorded>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Scans[ev.ScanId] = new DataQualityScanRecord
                    {
                        ScanId = ev.ScanId,
                        StartedAt = ev.StartedAt,
                        CompletedAt = ev.CompletedAt,
                        AppVersion = ev.AppVersion,
                        RuleSetVersion = ev.RuleSetVersion,
                        SummaryJson = ev.SummaryJson
                    };
                    break;
                }
                case nameof(DataQualityIssueAcknowledged):
                {
                    var ev = JsonSerializer.Deserialize<DataQualityIssueAcknowledged>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.AcknowledgedIssueIds.Add(ev.IssueId);
                    break;
                }
                case nameof(DataQualityIssueResolved):
                {
                    var ev = JsonSerializer.Deserialize<DataQualityIssueResolved>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.ResolvedIssueIds.Add(ev.IssueId);
                    break;
                }
                case nameof(DataQualityAutoFixApplied):
                {
                    var ev = JsonSerializer.Deserialize<DataQualityAutoFixApplied>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (!state.AppliedFixesByIssue.TryGetValue(ev.IssueId, out var fixes))
                    {
                        fixes = new List<Guid>();
                        state.AppliedFixesByIssue[ev.IssueId] = fixes;
                    }
                    fixes.Add(ev.FixId);
                    break;
                }
            }
        }

        return state;
    }
}
