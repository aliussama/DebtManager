using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Notifications;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

public static class NotificationsProjector
{
    public static NotificationsState Project(IEnumerable<EventEnvelope> envelopes)
    {
        var state = new NotificationsState();
        var opt = DomainJson.Options;

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value);

        foreach (var env in ordered)
        {
            switch (env.EventType)
            {
                case nameof(NotificationRuleCreated):
                {
                    var ev = JsonSerializer.Deserialize<NotificationRuleCreated>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Rules[ev.RuleId] = new NotificationRuleRecord
                    {
                        RuleId = ev.RuleId,
                        RuleCode = ev.RuleCode,
                        Area = ev.Area,
                        Severity = ev.Severity,
                        ConfigJson = ev.ConfigJson,
                        IsEnabled = ev.IsEnabled
                    };
                    break;
                }
                case nameof(NotificationRuleModified):
                {
                    var ev = JsonSerializer.Deserialize<NotificationRuleModified>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Rules.TryGetValue(ev.RuleId, out var r))
                    {
                        r.ConfigJson = ev.ConfigJson;
                        r.IsEnabled = ev.IsEnabled;
                    }
                    break;
                }
                case nameof(NotificationRuleArchived):
                {
                    var ev = JsonSerializer.Deserialize<NotificationRuleArchived>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Rules.TryGetValue(ev.RuleId, out var r))
                        r.IsArchived = true;
                    break;
                }
                case nameof(NotificationAcknowledged):
                {
                    var ev = JsonSerializer.Deserialize<NotificationAcknowledged>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Decisions[ev.NotificationId] = new NotificationDecision
                    {
                        NotificationId = ev.NotificationId,
                        Status = "Acknowledged",
                        OccurredAt = env.OccurredAt,
                        Note = ev.AckNote
                    };
                    break;
                }
                case nameof(NotificationDismissed):
                {
                    var ev = JsonSerializer.Deserialize<NotificationDismissed>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Decisions[ev.NotificationId] = new NotificationDecision
                    {
                        NotificationId = ev.NotificationId,
                        Status = "Dismissed",
                        OccurredAt = env.OccurredAt,
                        Note = ev.Reason
                    };
                    break;
                }
                case nameof(NotificationSnoozed):
                {
                    var ev = JsonSerializer.Deserialize<NotificationSnoozed>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Decisions[ev.NotificationId] = new NotificationDecision
                    {
                        NotificationId = ev.NotificationId,
                        Status = "Snoozed",
                        SnoozeUntil = ev.SnoozeUntil,
                        OccurredAt = env.OccurredAt,
                        Note = ev.Reason
                    };
                    break;
                }
                case nameof(NotificationActionLinked):
                {
                    var ev = JsonSerializer.Deserialize<NotificationActionLinked>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.LinkedActions[ev.NotificationId] = (ev.ActionType, ev.ActionRefJson);
                    break;
                }
            }
        }

        return state;
    }
}
