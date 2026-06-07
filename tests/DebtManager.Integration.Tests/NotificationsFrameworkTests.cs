using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Notifications;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class NotificationsFrameworkTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public NotificationsFrameworkTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"NotifTests_{id}.db");
        _factory = new SqliteConnectionFactory(_dbPath, new TestKeyStore());
        _eventStore = new SqliteEventStore(_factory);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        for (int i = 0; i < 30; i++)
        {
            try
            {
                if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
                if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
                if (File.Exists(_dbPath)) File.Delete(_dbPath);
                break;
            }
            catch (IOException) when (i < 29) { Thread.Sleep(100); }
        }
    }

    // ----------------------------------------------------------------
    // 1) Rules_CreateModifyArchive_Works
    // ----------------------------------------------------------------
    [Fact]
    public async Task Rules_CreateModifyArchive_Works()
    {
        var createHandler = new CreateNotificationRuleHandler(_eventStore);
        var modifyHandler = new ModifyNotificationRuleHandler(_eventStore);
        var archiveHandler = new ArchiveNotificationRuleHandler(_eventStore);
        var listHandler = new GetNotificationRulesHandler(_eventStore);

        var ruleId = await createHandler.HandleAsync(
            new CreateNotificationRuleCommand(null, "REM-BILL-DUE", "Billing", "Warning",
                "{\"DaysBefore\":5}", true, new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var rules = await listHandler.HandleAsync(false, CancellationToken.None);
        Assert.Single(rules);
        Assert.Equal("REM-BILL-DUE", rules[0].RuleCode);
        Assert.True(rules[0].IsEnabled);

        // Modify
        await modifyHandler.HandleAsync(
            new ModifyNotificationRuleCommand(ruleId, "{\"DaysBefore\":7}", false, new DateOnly(2025, 2, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        rules = await listHandler.HandleAsync(false, CancellationToken.None);
        Assert.False(rules[0].IsEnabled);

        // Archive
        await archiveHandler.HandleAsync(
            new ArchiveNotificationRuleCommand(ruleId, "No longer needed", new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        rules = await listHandler.HandleAsync(false, CancellationToken.None);
        Assert.Empty(rules);
    }

    // ----------------------------------------------------------------
    // 2) NotificationCenter_BillsDue_GeneratesCandidates
    // ----------------------------------------------------------------
    [Fact]
    public async Task NotificationCenter_BillsDue_GeneratesCandidates()
    {
        await CreateRule("REM-BILL-DUE", "Billing", "Warning", "{\"DaysBefore\":5}");
        var partyId = await CreateParty("Vendor X", "Vendor");

        await IssueBill(partyId, 500m, "EGP", new DateOnly(2025, 6, 4), new DateOnly(2025, 6, 1));

        var handler = new GetNotificationCenterHandler(_eventStore);
        var center = await handler.HandleAsync(new DateOnly(2025, 6, 1), false, CancellationToken.None);

        Assert.True(center.Notifications.Count >= 1);
        Assert.Contains(center.Notifications, n => n.RuleCode == "REM-BILL-DUE");
    }

    // ----------------------------------------------------------------
    // 3) NotificationCenter_BillsOverdue_GeneratesCandidates
    // ----------------------------------------------------------------
    [Fact]
    public async Task NotificationCenter_BillsOverdue_GeneratesCandidates()
    {
        await CreateRule("REM-BILL-OVERDUE", "Billing", "Error", "{}");
        var partyId = await CreateParty("Vendor Y", "Vendor");

        await IssueBill(partyId, 1000m, "EGP", new DateOnly(2025, 5, 15), new DateOnly(2025, 5, 1));

        var handler = new GetNotificationCenterHandler(_eventStore);
        var center = await handler.HandleAsync(new DateOnly(2025, 6, 1), false, CancellationToken.None);

        Assert.Contains(center.Notifications, n => n.RuleCode == "REM-BILL-OVERDUE");
        Assert.True(center.Summary.ErrorCount > 0);
    }

    // ----------------------------------------------------------------
    // 4) NotificationCenter_InvoiceOverdue_GeneratesCandidates
    // ----------------------------------------------------------------
    [Fact]
    public async Task NotificationCenter_InvoiceOverdue_GeneratesCandidates()
    {
        await CreateRule("REM-INV-OVERDUE", "Billing", "Error", "{}");
        var partyId = await CreateParty("Customer Z", "Customer");

        await IssueInvoice(partyId, 2000m, "EGP", new DateOnly(2025, 5, 10), new DateOnly(2025, 5, 1));

        var handler = new GetNotificationCenterHandler(_eventStore);
        var center = await handler.HandleAsync(new DateOnly(2025, 6, 1), false, CancellationToken.None);

        Assert.Contains(center.Notifications, n => n.RuleCode == "REM-INV-OVERDUE");
    }

    // ----------------------------------------------------------------
    // 5) NotificationCenter_ContractEnding_GeneratesCandidates
    // ----------------------------------------------------------------
    [Fact]
    public async Task NotificationCenter_ContractEnding_GeneratesCandidates()
    {
        await CreateRule("REM-CONTRACT-ENDING", "Billing", "Warning", "{\"DaysBefore\":30}");
        var partyId = await CreateParty("ISP", "Vendor");

        var termsJson = JsonSerializer.Serialize(new { BillingCycle = "Monthly", BillingInterval = 1, BillingDayOfMonth = 1, BaseAmount = 200m, Category = "Internet" });
        var createHandler = new CreateContractHandler(_eventStore);
        await createHandler.HandleAsync(
            new CreateContractCommand(null, partyId, "Utilities", "Internet",
                new DateOnly(2024, 1, 1), new DateOnly(2025, 6, 15), "EGP", termsJson,
                new DateOnly(2024, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var handler = new GetNotificationCenterHandler(_eventStore);
        var center = await handler.HandleAsync(new DateOnly(2025, 6, 1), false, CancellationToken.None);

        Assert.Contains(center.Notifications, n => n.RuleCode == "REM-CONTRACT-ENDING");
    }

    // ----------------------------------------------------------------
    // 6) NotificationCenter_BudgetThreshold_Warns
    // ----------------------------------------------------------------
    [Fact]
    public async Task NotificationCenter_BudgetThreshold_Warns()
    {
        await CreateRule("REM-BUDGET-THRESHOLD", "Budgets", "Warning", "{\"Threshold\":80}");

        // Create a category + budget + expenses exceeding threshold
        var categoryId = Guid.NewGuid();
        await AppendEvent(new CategoryCreated(categoryId, "Food", "expense", null, DateOnly.FromDateTime(DateTime.Today)));

        var budgetId = Guid.NewGuid();
        await AppendEvent(new BudgetDefined(budgetId, 2025, 6, "EGP", "category", categoryId, null, 1000m,
            "none", new DateOnly(2025, 6, 1)));

        var accountId = await CreateAccount("Cash", "EGP", new DateOnly(2025, 1, 1));
        // Record expense of 900 (90% of 1000 budget)
        await AppendEvent(new ExpenseRecorded(accountId, new Money(900m, new Currency("EGP", 2)),
            new DateOnly(2025, 6, 15), "Food", "Groceries"));

        var handler = new GetNotificationCenterHandler(_eventStore);
        var center = await handler.HandleAsync(new DateOnly(2025, 6, 20), false, CancellationToken.None);

        Assert.Contains(center.Notifications, n => n.RuleCode == "REM-BUDGET-THRESHOLD");
    }

    // ----------------------------------------------------------------
    // 7) NotificationCenter_ForecastNegativeBalance_Warns
    // ----------------------------------------------------------------
    [Fact]
    public async Task NotificationCenter_ForecastNegativeBalance_Warns()
    {
        await CreateRule("REM-FORECAST-NEGATIVE", "Forecast", "Warning", "{}");

        var accountId = await CreateAccount("Checking", "EGP", new DateOnly(2025, 1, 1));
        // Record big expense to make balance negative
        await AppendEvent(new ExpenseRecorded(accountId, new Money(5000m, new Currency("EGP", 2)),
            new DateOnly(2025, 6, 1), "General", "Large purchase"));

        var handler = new GetNotificationCenterHandler(_eventStore);
        var center = await handler.HandleAsync(new DateOnly(2025, 6, 5), false, CancellationToken.None);

        Assert.Contains(center.Notifications, n => n.RuleCode == "REM-FORECAST-NEGATIVE");
    }

    // ----------------------------------------------------------------
    // 8) Acknowledge_WritesEvent_AndMarksStatus
    // ----------------------------------------------------------------
    [Fact]
    public async Task Acknowledge_WritesEvent_AndMarksStatus()
    {
        await CreateRule("REM-BILL-OVERDUE", "Billing", "Error", "{}");
        var partyId = await CreateParty("Vendor Ack", "Vendor");
        await IssueBill(partyId, 500m, "EGP", new DateOnly(2025, 5, 1), new DateOnly(2025, 4, 1));

        var centerHandler = new GetNotificationCenterHandler(_eventStore);
        var center = await centerHandler.HandleAsync(new DateOnly(2025, 6, 1), false, CancellationToken.None);
        var notifId = center.Notifications.First(n => n.RuleCode == "REM-BILL-OVERDUE").NotificationId;

        var ackHandler = new AcknowledgeNotificationHandler(_eventStore);
        await ackHandler.HandleAsync(
            new AcknowledgeNotificationCommand(notifId, "Noted", new DateOnly(2025, 6, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Verify event written
        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        Assert.Contains(envelopes, e => e.EventType == nameof(NotificationAcknowledged));

        // Without includeAcknowledged, notification should be hidden
        var center2 = await centerHandler.HandleAsync(new DateOnly(2025, 6, 1), false, CancellationToken.None);
        Assert.DoesNotContain(center2.Notifications, n => n.NotificationId == notifId);

        // With includeAcknowledged, it should appear
        var center3 = await centerHandler.HandleAsync(new DateOnly(2025, 6, 1), true, CancellationToken.None);
        var found = center3.Notifications.FirstOrDefault(n => n.NotificationId == notifId);
        Assert.NotNull(found);
        Assert.Equal("Acknowledged", found!.Status);
    }

    // ----------------------------------------------------------------
    // 9) Dismiss_WritesEvent_AndHidesNotification
    // ----------------------------------------------------------------
    [Fact]
    public async Task Dismiss_WritesEvent_AndHidesNotification()
    {
        await CreateRule("REM-BILL-OVERDUE", "Billing", "Error", "{}");
        var partyId = await CreateParty("Vendor Dismiss", "Vendor");
        await IssueBill(partyId, 300m, "EGP", new DateOnly(2025, 5, 1), new DateOnly(2025, 4, 1));

        var centerHandler = new GetNotificationCenterHandler(_eventStore);
        var center = await centerHandler.HandleAsync(new DateOnly(2025, 6, 1), false, CancellationToken.None);
        var notifId = center.Notifications.First(n => n.RuleCode == "REM-BILL-OVERDUE").NotificationId;

        var dismissHandler = new DismissNotificationHandler(_eventStore);
        await dismissHandler.HandleAsync(
            new DismissNotificationCommand(notifId, "Not relevant", new DateOnly(2025, 6, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var center2 = await centerHandler.HandleAsync(new DateOnly(2025, 6, 1), true, CancellationToken.None);
        Assert.DoesNotContain(center2.Notifications, n => n.NotificationId == notifId);
    }

    // ----------------------------------------------------------------
    // 10) Snooze_WritesEvent_AndHidesUntilDate
    // ----------------------------------------------------------------
    [Fact]
    public async Task Snooze_WritesEvent_AndHidesUntilDate()
    {
        await CreateRule("REM-BILL-OVERDUE", "Billing", "Error", "{}");
        var partyId = await CreateParty("Vendor Snooze", "Vendor");
        await IssueBill(partyId, 400m, "EGP", new DateOnly(2025, 5, 1), new DateOnly(2025, 4, 1));

        var centerHandler = new GetNotificationCenterHandler(_eventStore);
        var center = await centerHandler.HandleAsync(new DateOnly(2025, 6, 1), false, CancellationToken.None);
        var notifId = center.Notifications.First(n => n.RuleCode == "REM-BILL-OVERDUE").NotificationId;

        var snoozeHandler = new SnoozeNotificationHandler(_eventStore);
        await snoozeHandler.HandleAsync(
            new SnoozeNotificationCommand(notifId, new DateOnly(2025, 6, 15), "Check later",
                new DateOnly(2025, 6, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Hidden before snooze-until
        var center2 = await centerHandler.HandleAsync(new DateOnly(2025, 6, 10), false, CancellationToken.None);
        Assert.DoesNotContain(center2.Notifications, n => n.NotificationId == notifId);

        // Visible after snooze-until
        var center3 = await centerHandler.HandleAsync(new DateOnly(2025, 6, 16), false, CancellationToken.None);
        Assert.Contains(center3.Notifications, n => n.NotificationId == notifId);
    }

    // ----------------------------------------------------------------
    // 11) Determinism_SameEventsSameAsOfDate_SameNotificationIds
    // ----------------------------------------------------------------
    [Fact]
    public async Task Determinism_SameEventsSameAsOfDate_SameNotificationIds()
    {
        await CreateRule("REM-BILL-OVERDUE", "Billing", "Error", "{}");
        var partyId = await CreateParty("Vendor Determ", "Vendor");
        await IssueBill(partyId, 700m, "EGP", new DateOnly(2025, 5, 1), new DateOnly(2025, 4, 1));

        var handler = new GetNotificationCenterHandler(_eventStore);
        var center1 = await handler.HandleAsync(new DateOnly(2025, 6, 1), false, CancellationToken.None);
        var center2 = await handler.HandleAsync(new DateOnly(2025, 6, 1), false, CancellationToken.None);

        Assert.Equal(center1.Notifications.Count, center2.Notifications.Count);
        for (int i = 0; i < center1.Notifications.Count; i++)
        {
            Assert.Equal(center1.Notifications[i].NotificationId, center2.Notifications[i].NotificationId);
        }
    }

    // ----------------------------------------------------------------
    // 12) Decisions_AppliedCorrectly_AfterRecompute
    // ----------------------------------------------------------------
    [Fact]
    public async Task Decisions_AppliedCorrectly_AfterRecompute()
    {
        await CreateRule("REM-BILL-OVERDUE", "Billing", "Error", "{}");
        var partyId = await CreateParty("Vendor Recomp", "Vendor");

        // Issue 2 overdue bills
        await IssueBill(partyId, 100m, "EGP", new DateOnly(2025, 5, 1), new DateOnly(2025, 4, 1));
        await IssueBill(partyId, 200m, "EGP", new DateOnly(2025, 5, 5), new DateOnly(2025, 4, 5));

        var centerHandler = new GetNotificationCenterHandler(_eventStore);
        var center = await centerHandler.HandleAsync(new DateOnly(2025, 6, 1), false, CancellationToken.None);
        Assert.Equal(2, center.Notifications.Count);

        // Dismiss the first
        var firstId = center.Notifications[0].NotificationId;
        var dismissHandler = new DismissNotificationHandler(_eventStore);
        await dismissHandler.HandleAsync(
            new DismissNotificationCommand(firstId, "Done", new DateOnly(2025, 6, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Recompute - should only have 1
        var center2 = await centerHandler.HandleAsync(new DateOnly(2025, 6, 1), false, CancellationToken.None);
        Assert.Single(center2.Notifications);
        Assert.NotEqual(firstId, center2.Notifications[0].NotificationId);
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private async Task<Guid> CreateRule(string ruleCode, string area, string severity, string configJson)
    {
        var handler = new CreateNotificationRuleHandler(_eventStore);
        return await handler.HandleAsync(
            new CreateNotificationRuleCommand(null, ruleCode, area, severity, configJson, true,
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> CreateParty(string name, string kind)
    {
        var handler = new CreatePartyHandler(_eventStore);
        return await handler.HandleAsync(
            new CreatePartyCommand(null, kind, name, "EGP", null, [],
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> IssueBill(Guid partyId, decimal amount, string ccy, DateOnly dueDate, DateOnly effectiveDate)
    {
        var handler = new IssueBillHandler(_eventStore);
        return await handler.HandleAsync(
            new IssueBillCommand(null, null, partyId, ccy, amount, dueDate, "General",
                $"BILL-{Guid.NewGuid().ToString("N")[..6]}", null, effectiveDate),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> IssueInvoice(Guid partyId, decimal amount, string ccy, DateOnly dueDate, DateOnly effectiveDate)
    {
        var handler = new IssueInvoiceHandler(_eventStore);
        return await handler.HandleAsync(
            new IssueInvoiceCommand(null, null, partyId, ccy, amount, dueDate, "Services",
                $"INV-{Guid.NewGuid().ToString("N")[..6]}", null, effectiveDate),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> CreateAccount(string name, string ccy, DateOnly? effectiveDate = null)
    {
        var accountId = Guid.NewGuid();
        var date = effectiveDate ?? DateOnly.FromDateTime(DateTime.Today);
        var ev = new AccountCreated(accountId, name, "Cash", ccy, 0m, date);
        await _eventStore.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(accountId),
            nameof(AccountCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            _actorUserId, _deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), CancellationToken.None);
        return accountId;
    }

    private async Task AppendEvent<T>(T domainEvent) where T : IDomainEvent
    {
        var ev = domainEvent;
        var typeName = typeof(T).Name;
        await _eventStore.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(Guid.NewGuid()),
            typeName, DateTimeOffset.UtcNow, ev.EffectiveDate,
            _actorUserId, _deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), CancellationToken.None);
    }
}
