using DebtManager.Domain.Notifications;

namespace DebtManager.Domain.Projections;

public sealed class NotificationsState
{
    public Dictionary<Guid, NotificationRuleRecord> Rules { get; } = new();
    public Dictionary<Guid, NotificationDecision> Decisions { get; } = new();
    public Dictionary<Guid, (string ActionType, string ActionRefJson)> LinkedActions { get; } = new();
}
