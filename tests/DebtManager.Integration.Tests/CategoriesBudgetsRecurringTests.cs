using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class CategoriesBudgetsRecurringTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public CategoriesBudgetsRecurringTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"CatBudRecTests_{id}.db");
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

    // ??? 1) Categories ???

    [Fact]
    public async Task Categories_CreateRenameArchive_Works()
    {
        var createHandler = new CreateCategoryHandler(_eventStore);
        var renameHandler = new RenameCategoryHandler(_eventStore);
        var archiveHandler = new ArchiveCategoryHandler(_eventStore);
        var listHandler = new GetCategoriesListHandler(_eventStore);

        // Create
        var catId = await createHandler.HandleAsync(
            new CreateCategoryCommand(null, "Groceries", "expense", null),
            _actorUserId, _deviceId, CancellationToken.None);

        var list = await listHandler.HandleAsync(CancellationToken.None);
        Assert.Single(list);
        Assert.Equal("Groceries", list[0].Name);
        Assert.Equal("expense", list[0].Kind);
        Assert.False(list[0].IsArchived);

        // Rename
        await renameHandler.HandleAsync(
            new RenameCategoryCommand(catId, "Food & Groceries"),
            _actorUserId, _deviceId, CancellationToken.None);

        list = await listHandler.HandleAsync(CancellationToken.None);
        Assert.Equal("Food & Groceries", list[0].Name);

        // Archive
        await archiveHandler.HandleAsync(
            new ArchiveCategoryCommand(catId, "Not needed"),
            _actorUserId, _deviceId, CancellationToken.None);

        list = await listHandler.HandleAsync(CancellationToken.None);
        Assert.True(list[0].IsArchived);
    }

    // ??? 2-3) Budget Dashboard ???

    [Fact]
    public async Task BudgetDefined_ShowsInBudgetDashboard()
    {
        var defineHandler = new DefineBudgetHandler(_eventStore);
        var dashHandler = new GetBudgetDashboardHandler(_eventStore);

        // Create a category first
        var catId = await new CreateCategoryHandler(_eventStore).HandleAsync(
            new CreateCategoryCommand(null, "Transport", "expense", null),
            _actorUserId, _deviceId, CancellationToken.None);

        // Define budget
        await defineHandler.HandleAsync(
            new DefineBudgetCommand(null, 2025, 7, "EGP", "category", catId, null, 2000m, "None"),
            _actorUserId, _deviceId, CancellationToken.None);

        var dash = await dashHandler.HandleAsync(
            new BudgetDashboardQuery(2025, 7), CancellationToken.None);

        Assert.Single(dash.Utilizations);
        Assert.Equal(2000m, dash.Utilizations[0].LimitAmount);
        Assert.Equal(0m, dash.Utilizations[0].ActualAmount);
        Assert.Equal("OK", dash.Utilizations[0].Status);
    }

    [Fact]
    public async Task BudgetDashboard_ComputesActualFromExpenses()
    {
        // Create account + category
        var accountId = await new CreateAccountHandler(_eventStore).HandleAsync(
            new CreateAccountCommand(null, "Wallet", "Cash", 5000m, "EGP", new DateOnly(2025, 7, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var catId = await new CreateCategoryHandler(_eventStore).HandleAsync(
            new CreateCategoryCommand(null, "Food", "expense", null),
            _actorUserId, _deviceId, CancellationToken.None);

        // Define budget for Food category: 1000 EGP
        await new DefineBudgetHandler(_eventStore).HandleAsync(
            new DefineBudgetCommand(null, 2025, 7, "EGP", "category", catId, null, 1000m, "None"),
            _actorUserId, _deviceId, CancellationToken.None);

        // Record expenses in the Food category
        await AppendExpenseEvent(accountId, 300m, "EGP", "Food", "Lunch", new DateOnly(2025, 7, 10));
        await AppendExpenseEvent(accountId, 200m, "EGP", "Food", "Dinner", new DateOnly(2025, 7, 15));

        var dash = await new GetBudgetDashboardHandler(_eventStore).HandleAsync(
            new BudgetDashboardQuery(2025, 7), CancellationToken.None);

        Assert.Single(dash.Utilizations);
        var u = dash.Utilizations[0];
        Assert.Equal(500m, u.ActualAmount);
        Assert.Equal(500m, u.RemainingAmount);
        Assert.Equal(50m, u.PercentUsed);
        Assert.Equal("OK", u.Status);
    }

    // ??? 4-5) Carry Policies ???

    [Fact]
    public async Task Budget_CarryPolicy_None_DoesNotCarry()
    {
        var catId = await new CreateCategoryHandler(_eventStore).HandleAsync(
            new CreateCategoryCommand(null, "Transport", "expense", null),
            _actorUserId, _deviceId, CancellationToken.None);

        var accountId = await new CreateAccountHandler(_eventStore).HandleAsync(
            new CreateAccountCommand(null, "Wallet", "Cash", 5000m, "EGP", new DateOnly(2025, 6, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // June budget: 1000, spend 600 (leaves 400 unused)
        await new DefineBudgetHandler(_eventStore).HandleAsync(
            new DefineBudgetCommand(null, 2025, 6, "EGP", "category", catId, null, 1000m, "None"),
            _actorUserId, _deviceId, CancellationToken.None);
        await AppendExpenseEvent(accountId, 600m, "EGP", "Transport", "Taxi", new DateOnly(2025, 6, 15));

        // July budget: 1000, None carry
        await new DefineBudgetHandler(_eventStore).HandleAsync(
            new DefineBudgetCommand(null, 2025, 7, "EGP", "category", catId, null, 1000m, "None"),
            _actorUserId, _deviceId, CancellationToken.None);

        var dash = await new GetBudgetDashboardHandler(_eventStore).HandleAsync(
            new BudgetDashboardQuery(2025, 7), CancellationToken.None);

        var jul = dash.Utilizations.Single(u => u.LimitAmount == 1000m);
        Assert.Equal(1000m, jul.LimitAmount); // No carry
    }

    [Fact]
    public async Task Budget_CarryPolicy_CarryUnused_CarriesForward()
    {
        var catId = await new CreateCategoryHandler(_eventStore).HandleAsync(
            new CreateCategoryCommand(null, "Entertainment", "expense", null),
            _actorUserId, _deviceId, CancellationToken.None);

        var accountId = await new CreateAccountHandler(_eventStore).HandleAsync(
            new CreateAccountCommand(null, "Cash", "Cash", 10000m, "EGP", new DateOnly(2025, 6, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // June budget: 1000, spend only 300 (leaves 700 unused)
        await new DefineBudgetHandler(_eventStore).HandleAsync(
            new DefineBudgetCommand(null, 2025, 6, "EGP", "category", catId, null, 1000m, "CarryUnused"),
            _actorUserId, _deviceId, CancellationToken.None);
        await AppendExpenseEvent(accountId, 300m, "EGP", "Entertainment", "Movie", new DateOnly(2025, 6, 15));

        // July budget: 1000, CarryUnused
        await new DefineBudgetHandler(_eventStore).HandleAsync(
            new DefineBudgetCommand(null, 2025, 7, "EGP", "category", catId, null, 1000m, "CarryUnused"),
            _actorUserId, _deviceId, CancellationToken.None);

        var dash = await new GetBudgetDashboardHandler(_eventStore).HandleAsync(
            new BudgetDashboardQuery(2025, 7), CancellationToken.None);

        // July should have 1000 + 700 = 1700 effective limit
        var jul = dash.Utilizations.Single();
        Assert.Equal(1700m, jul.LimitAmount);
    }

    // ??? 6-10) Recurring Transactions ???

    [Fact]
    public async Task Recurring_Create_ComputesNextDue()
    {
        var accountId = await new CreateAccountHandler(_eventStore).HandleAsync(
            new CreateAccountCommand(null, "Salary Account", "Bank", 0m, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var createHandler = new CreateRecurringHandler(_eventStore);
        var dashHandler = new GetRecurringDashboardHandler(_eventStore);

        await createHandler.HandleAsync(
            new CreateRecurringCommand(null, "income", accountId, 15000m, "EGP", null,
                null, "Salary", "Monthly", 1, new DateOnly(2025, 1, 1), null, false),
            _actorUserId, _deviceId, CancellationToken.None);

        var result = await dashHandler.HandleAsync(new DateOnly(2025, 3, 15), null, CancellationToken.None);

        Assert.Single(result.Items);
        var item = result.Items[0];
        Assert.Equal("income", item.Kind);
        Assert.Equal(15000m, item.Amount);
        // Next due should be Jan 1 (first unposted)
        Assert.NotNull(item.NextDueDate);
        Assert.Equal(new DateOnly(2025, 1, 1), item.NextDueDate);
        Assert.Equal("Overdue", item.Status); // Jan 1 < Mar 15
    }

    [Fact]
    public async Task Recurring_PostNow_CreatesExpenseOrIncomeEvent()
    {
        var accountId = await new CreateAccountHandler(_eventStore).HandleAsync(
            new CreateAccountCommand(null, "Bank", "Bank", 10000m, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        await new CreateRecurringHandler(_eventStore).HandleAsync(
            new CreateRecurringCommand(null, "expense", accountId, 500m, "EGP", null,
                "Monthly rent", "Rent", "Monthly", 1, new DateOnly(2025, 1, 1), null, false),
            _actorUserId, _deviceId, CancellationToken.None);

        var recurringItems = await new GetRecurringDashboardHandler(_eventStore).HandleAsync(
            new DateOnly(2025, 1, 15), null, CancellationToken.None);
        var recurringId = recurringItems.Items[0].RecurringId;

        // Post now
        var postHandler = new PostRecurringNowHandler(_eventStore);
        await postHandler.HandleAsync(
            new PostRecurringNowCommand(recurringId, new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Verify balance decreased
        var accounts = await new GetAccountsListHandler(_eventStore).HandleAsync(CancellationToken.None);
        var account = accounts.Single(a => a.AccountId == accountId);
        Assert.Equal(9500m, account.Balance); // 10000 - 500
    }

    [Fact]
    public async Task Recurring_PostNow_AppendsPostedLinkEvent()
    {
        var accountId = await new CreateAccountHandler(_eventStore).HandleAsync(
            new CreateAccountCommand(null, "Bank", "Bank", 5000m, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var recurringId = await new CreateRecurringHandler(_eventStore).HandleAsync(
            new CreateRecurringCommand(null, "income", accountId, 3000m, "EGP", null,
                null, "Salary", "Monthly", 1, new DateOnly(2025, 1, 1), null, false),
            _actorUserId, _deviceId, CancellationToken.None);

        var postHandler = new PostRecurringNowHandler(_eventStore);
        await postHandler.HandleAsync(
            new PostRecurringNowCommand(recurringId, new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Check that RecurringTransactionPosted event exists in the stream
        var allEnvelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var postedEvents = allEnvelopes.Where(e => e.EventType == nameof(RecurringTransactionPosted)).ToList();

        Assert.Single(postedEvents);
        var posted = JsonSerializer.Deserialize<RecurringTransactionPosted>(
            postedEvents[0].PayloadJson, DomainJson.Options);
        Assert.NotNull(posted);
        Assert.Equal(recurringId, posted!.RecurringId);
    }

    [Fact]
    public async Task Recurring_PostNow_TwiceSameCycle_Blocked()
    {
        var accountId = await new CreateAccountHandler(_eventStore).HandleAsync(
            new CreateAccountCommand(null, "Bank", "Bank", 5000m, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var recurringId = await new CreateRecurringHandler(_eventStore).HandleAsync(
            new CreateRecurringCommand(null, "expense", accountId, 200m, "EGP", null,
                null, "Sub", "Monthly", 1, new DateOnly(2025, 1, 1), null, false),
            _actorUserId, _deviceId, CancellationToken.None);

        var postHandler = new PostRecurringNowHandler(_eventStore);

        // First post: should succeed
        await postHandler.HandleAsync(
            new PostRecurringNowCommand(recurringId, new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Second post for same cycle: should throw
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            postHandler.HandleAsync(
                new PostRecurringNowCommand(recurringId, new DateOnly(2025, 1, 1)),
                _actorUserId, _deviceId, CancellationToken.None));

        Assert.Contains("Already posted", ex.Message);
    }

    [Fact]
    public async Task Recurring_Archived_NotPostable()
    {
        var accountId = await new CreateAccountHandler(_eventStore).HandleAsync(
            new CreateAccountCommand(null, "Bank", "Bank", 5000m, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var recurringId = await new CreateRecurringHandler(_eventStore).HandleAsync(
            new CreateRecurringCommand(null, "expense", accountId, 100m, "EGP", null,
                null, "Gym", "Monthly", 1, new DateOnly(2025, 1, 1), null, false),
            _actorUserId, _deviceId, CancellationToken.None);

        // Archive
        await new ArchiveRecurringHandler(_eventStore).HandleAsync(
            new ArchiveRecurringCommand(recurringId, "Cancelled"),
            _actorUserId, _deviceId, CancellationToken.None);

        // Verify archived shows in dashboard
        var dash = await new GetRecurringDashboardHandler(_eventStore).HandleAsync(
            new DateOnly(2025, 1, 15), null, CancellationToken.None);
        Assert.True(dash.Items[0].IsArchived);
        Assert.Equal("Ended", dash.Items[0].Status);

        // Attempt to post: should throw
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PostRecurringNowHandler(_eventStore).HandleAsync(
                new PostRecurringNowCommand(recurringId, new DateOnly(2025, 1, 1)),
                _actorUserId, _deviceId, CancellationToken.None));

        Assert.Contains("archived", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ??? helpers ???

    private async Task AppendExpenseEvent(Guid accountId, decimal amount, string currency, string category, string notes, DateOnly? date = null)
    {
        var effectiveDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var currencyObj = currency switch
        {
            "EGP" => Currency.EGP,
            "USD" => Currency.USD,
            _ => new Currency(currency, 2)
        };
        var ev = new ExpenseRecorded(accountId, new Money(amount, currencyObj), effectiveDate, category, notes);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(accountId),
            nameof(ExpenseRecorded), DateTimeOffset.UtcNow, effectiveDate,
            _actorUserId, _deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);
    }
}
