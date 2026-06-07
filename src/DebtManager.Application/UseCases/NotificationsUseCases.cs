using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Notifications;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Well-known stream ---
public static class NotificationStreams
{
    public static readonly StreamId NotificationStream = new(Guid.Parse("A0E1F1C0-0036-0036-0036-000000000001"));
}

// --- Commands ---

public sealed record CreateNotificationRuleCommand(
    Guid? RuleId, string RuleCode, string Area, string Severity,
    string ConfigJson, bool IsEnabled, DateOnly EffectiveDate);

public sealed record ModifyNotificationRuleCommand(
    Guid RuleId, string ConfigJson, bool IsEnabled, DateOnly EffectiveDate);

public sealed record ArchiveNotificationRuleCommand(Guid RuleId, string Reason, DateOnly EffectiveDate);

public sealed record AcknowledgeNotificationCommand(Guid NotificationId, string AckNote, DateOnly EffectiveDate);

public sealed record DismissNotificationCommand(Guid NotificationId, string Reason, DateOnly EffectiveDate);

public sealed record SnoozeNotificationCommand(Guid NotificationId, DateOnly SnoozeUntil, string Reason, DateOnly EffectiveDate);

public sealed record LinkNotificationActionCommand(
    Guid NotificationId, string ActionType, string ActionRefJson, DateOnly EffectiveDate);

// --- DTOs ---

public sealed record NotificationRuleDto(
    Guid RuleId, string RuleCode, string Area, string Severity,
    string ConfigJson, bool IsEnabled, bool IsArchived);

public sealed record NotificationItemDto(
    Guid NotificationId, string RuleCode, string Area, string Severity,
    string Title, string Body, DateOnly EffectiveDate, DateOnly? DueDate,
    string? RefJson, string Status, DateOnly? SnoozeUntil);

public sealed record NotificationCenterDto(
    NotificationsSummary Summary,
    IReadOnlyList<NotificationItemDto> Notifications);

// --- Handlers ---

public sealed class CreateNotificationRuleHandler
{
    private readonly IEventStore _store;
    public CreateNotificationRuleHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(CreateNotificationRuleCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        // Validate unique RuleCode among non-archived rules
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = NotificationsProjector.Project(all);
        if (state.Rules.Values.Any(r => !r.IsArchived && r.RuleCode == cmd.RuleCode))
            throw new InvalidOperationException($"A rule with code '{cmd.RuleCode}' already exists.");

        var id = cmd.RuleId ?? Guid.NewGuid();
        var ev = new NotificationRuleCreated(id, cmd.RuleCode, cmd.Area, cmd.Severity,
            cmd.ConfigJson, cmd.IsEnabled, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), NotificationStreams.NotificationStream,
            nameof(NotificationRuleCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
        return id;
    }
}

public sealed class ModifyNotificationRuleHandler
{
    private readonly IEventStore _store;
    public ModifyNotificationRuleHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ModifyNotificationRuleCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new NotificationRuleModified(cmd.RuleId, cmd.ConfigJson, cmd.IsEnabled, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), NotificationStreams.NotificationStream,
            nameof(NotificationRuleModified), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class ArchiveNotificationRuleHandler
{
    private readonly IEventStore _store;
    public ArchiveNotificationRuleHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveNotificationRuleCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new NotificationRuleArchived(cmd.RuleId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), NotificationStreams.NotificationStream,
            nameof(NotificationRuleArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class AcknowledgeNotificationHandler
{
    private readonly IEventStore _store;
    public AcknowledgeNotificationHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(AcknowledgeNotificationCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new NotificationAcknowledged(cmd.NotificationId, cmd.AckNote, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.NotificationId),
            nameof(NotificationAcknowledged), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class DismissNotificationHandler
{
    private readonly IEventStore _store;
    public DismissNotificationHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(DismissNotificationCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new NotificationDismissed(cmd.NotificationId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.NotificationId),
            nameof(NotificationDismissed), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class SnoozeNotificationHandler
{
    private readonly IEventStore _store;
    public SnoozeNotificationHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(SnoozeNotificationCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        if (cmd.SnoozeUntil < cmd.EffectiveDate)
            throw new InvalidOperationException("SnoozeUntil must be >= EffectiveDate.");

        var ev = new NotificationSnoozed(cmd.NotificationId, cmd.SnoozeUntil, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.NotificationId),
            nameof(NotificationSnoozed), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class LinkNotificationActionHandler
{
    private readonly IEventStore _store;
    public LinkNotificationActionHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(LinkNotificationActionCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new NotificationActionLinked(cmd.NotificationId, cmd.ActionType, cmd.ActionRefJson, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.NotificationId),
            nameof(NotificationActionLinked), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class GetNotificationRulesHandler
{
    private readonly IEventStore _store;
    public GetNotificationRulesHandler(IEventStore store) => _store = store;

    public async Task<IReadOnlyList<NotificationRuleDto>> HandleAsync(bool includeArchived, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = NotificationsProjector.Project(all);
        return state.Rules.Values
            .Where(r => includeArchived || !r.IsArchived)
            .Select(r => new NotificationRuleDto(r.RuleId, r.RuleCode, r.Area, r.Severity,
                r.ConfigJson, r.IsEnabled, r.IsArchived))
            .ToList();
    }
}

public sealed class GetNotificationCenterHandler
{
    private readonly IEventStore _store;
    public GetNotificationCenterHandler(IEventStore store) => _store = store;

    public async Task<NotificationCenterDto> HandleAsync(DateOnly asOfDate, bool includeAcknowledged, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);

        // Build required states
        var notifState = NotificationsProjector.Project(all);
        var billingState = BillingProjector.Project(all);
        var contractsState = ContractsProjector.Project(all);
        var budgetState = BudgetProjector.Project(all);
        var cashState = CashLedgerProjector.Project(all);
        var recurringState = RecurringProjector.Project(all);
        var taxState = TaxProjector.Project(all);
        var categoryState = CategoryProjector.Project(all);

        // Get active rules
        var activeRules = notifState.Rules.Values
            .Where(r => r.IsEnabled && !r.IsArchived)
            .ToList();

        // Generate candidates deterministically
        var candidates = NotificationRuleEngine.BuildCandidates(
            asOfDate, billingState, contractsState, budgetState,
            cashState, recurringState, taxState, categoryState, activeRules);

        // Apply decisions
        var items = new List<NotificationItemDto>();
        var overdueCount = 0;
        DateOnly? nextDue = null;

        foreach (var c in candidates)
        {
            var status = "Active";
            DateOnly? snoozeUntil = null;

            if (notifState.Decisions.TryGetValue(c.NotificationId, out var decision))
            {
                if (decision.Status == "Dismissed")
                    continue;

                if (decision.Status == "Snoozed")
                {
                    if (decision.SnoozeUntil.HasValue && decision.SnoozeUntil.Value > asOfDate)
                        continue;
                    // Snooze expired — show again as active
                    status = "Active";
                }
                else if (decision.Status == "Acknowledged")
                {
                    if (!includeAcknowledged) continue;
                    status = "Acknowledged";
                }
            }

            if (c.DueDate.HasValue && c.DueDate.Value < asOfDate)
                overdueCount++;

            if (c.DueDate.HasValue && c.DueDate.Value >= asOfDate)
            {
                if (!nextDue.HasValue || c.DueDate.Value < nextDue.Value)
                    nextDue = c.DueDate;
            }

            items.Add(new NotificationItemDto(
                c.NotificationId, c.RuleCode, c.Area, c.Severity,
                c.Title, c.Body, c.EffectiveDate, c.DueDate,
                c.RefJson, status, snoozeUntil));
        }

        // Sort: severity desc, due date asc
        var severityOrder = new Dictionary<string, int>
        {
            ["Critical"] = 0, ["Error"] = 1, ["Warning"] = 2, ["Info"] = 3
        };

        items = items
            .OrderBy(n => severityOrder.TryGetValue(n.Severity, out var o) ? o : 9)
            .ThenBy(n => n.DueDate ?? DateOnly.MaxValue)
            .ToList();

        var summary = new NotificationsSummary(
            TotalActive: items.Count,
            CriticalCount: items.Count(n => n.Severity == "Critical"),
            ErrorCount: items.Count(n => n.Severity == "Error"),
            WarningCount: items.Count(n => n.Severity == "Warning"),
            InfoCount: items.Count(n => n.Severity == "Info"),
            OverdueCount: overdueCount,
            NextDue: nextDue);

        return new NotificationCenterDto(summary, items);
    }
}
