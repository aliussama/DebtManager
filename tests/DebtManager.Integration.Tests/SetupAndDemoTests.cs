using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Desktop.ViewModels;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Rules;
using DebtManager.Infrastructure.Security;

namespace DebtManager.Integration.Tests;

public sealed class SetupAndDemoTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _configPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public SetupAndDemoTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"SetupAndDemoTests_{Guid.NewGuid()}.db");
        _configPath = Path.Combine(Path.GetTempPath(), $"SetupAndDemoTests_config_{Guid.NewGuid()}.json");
        _factory = new SqliteConnectionFactory(_dbPath, new TestKeyStore());
        _eventStore = new SqliteEventStore(_factory);
    }

    public void Dispose()
    {
        for (int i = 0; i < 30; i++)
        {
            try
            {
                if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
                if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
                if (File.Exists(_dbPath)) File.Delete(_dbPath);
                if (File.Exists(_configPath)) File.Delete(_configPath);
                break;
            }
            catch (IOException) when (i < 29) { Thread.Sleep(100); }
        }
    }

    [Fact]
    public async Task Setup_Completion_AppendsEvent()
    {
        var handler = new CompleteInitialSetupHandler(_eventStore);

        var setupId = await handler.HandleAsync("EGP", 1, false, false, false, _actorUserId, _deviceId, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, setupId);

        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state = SetupProjector.Project(envelopes);
        Assert.True(state.IsInitialSetupCompleted);
        Assert.Equal("EGP", state.ReportingCurrencyCode);
        Assert.Equal(1, state.FiscalYearStartMonth);
    }

    [Fact]
    public async Task DefaultAccounts_Created()
    {
        var createAccount = new CreateAccountHandler(_eventStore);
        var handler = new CreateDefaultAccountsHandler(_eventStore, createAccount);

        var ids = await handler.HandleAsync("EGP", Guid.NewGuid(), _actorUserId, _deviceId, CancellationToken.None);

        Assert.Equal(3, ids.Count);

        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state = SetupProjector.Project(envelopes);
        Assert.True(state.CreatedDefaultAccounts);

        // Verify accounts exist
        var cashState = CashLedgerProjector.Project(envelopes);
        Assert.True(cashState.Accounts.Count >= 3);
    }

    [Fact]
    public async Task DefaultCategories_Created()
    {
        var createCategory = new CreateCategoryHandler(_eventStore);
        var handler = new CreateDefaultCategoriesHandler(_eventStore, createCategory);

        var ids = await handler.HandleAsync(Guid.NewGuid(), _actorUserId, _deviceId, CancellationToken.None);

        Assert.True(ids.Count >= 12); // 4 income + 8 expense

        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state = SetupProjector.Project(envelopes);
        Assert.True(state.CreatedDefaultCategories);

        var catState = CategoryProjector.Project(envelopes);
        Assert.True(catState.Categories.Count >= 12);
    }

    [Fact]
    public async Task DemoData_Seeded_CreatesExpectedEntities()
    {
        var seedHandler = BuildSeedDemoHandler();
        var manifest = await seedHandler.HandleAsync("EGP", _actorUserId, _deviceId, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, manifest.DemoSeedId);

        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);

        // Setup state
        var setupState = SetupProjector.Project(envelopes);
        Assert.True(setupState.IsDemoModeActive);
        Assert.Equal(manifest.DemoSeedId, setupState.DemoSeedId);

        // Goals
        var goalsState = GoalsProjector.Project(envelopes);
        Assert.True(goalsState.Goals.Count >= 1);

        // Retirement
        var retState = RetirementProjector.Project(envelopes);
        Assert.NotNull(retState.ActiveProfile);
        Assert.NotNull(retState.ActiveAssumptions);

        // Obligations
        Assert.True(manifest.ObligationIds.Count >= 2);

        // Incomes + expenses
        Assert.True(manifest.IncomeEventIds.Count >= 2);
        Assert.True(manifest.ExpenseEventIds.Count >= 5);

        // Budget
        Assert.True(manifest.BudgetIds.Count >= 1);

        // Recurring
        Assert.True(manifest.RecurringIds.Count >= 1);

        // Assets
        Assert.True(manifest.AssetIds.Count >= 1);

        // Investment accounts
        Assert.True(manifest.InvestmentAccountIds.Count >= 1);
    }

    [Fact]
    public async Task DemoData_IsIdempotent()
    {
        var seedHandler = BuildSeedDemoHandler();

        var manifest1 = await seedHandler.HandleAsync("EGP", _actorUserId, _deviceId, CancellationToken.None);

        // Second call should return the same DemoSeedId (idempotent)
        var seedHandler2 = BuildSeedDemoHandler();
        var manifest2 = await seedHandler2.HandleAsync("EGP", _actorUserId, _deviceId, CancellationToken.None);

        Assert.Equal(manifest1.DemoSeedId, manifest2.DemoSeedId);
    }

    [Fact]
    public async Task ClearDemo_RemovesOnlyDemoData()
    {
        // Seed demo data
        var seedHandler = BuildSeedDemoHandler();
        var manifest = await seedHandler.HandleAsync("EGP", _actorUserId, _deviceId, CancellationToken.None);

        // Create user data (not part of demo)
        var createAccount = new CreateAccountHandler(_eventStore);
        var userAccountId = await createAccount.HandleAsync(
            new CreateAccountCommand(null, "My Personal Account", "Bank", 5000m, "EGP", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Clear demo
        var clearHandler = BuildClearDemoHandler();
        await clearHandler.HandleAsync(manifest, _actorUserId, _deviceId, CancellationToken.None);

        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);

        // Demo mode should be inactive
        var setupState = SetupProjector.Project(envelopes);
        Assert.False(setupState.IsDemoModeActive);
        Assert.Null(setupState.DemoSeedId);

        // User account should still exist (not archived)
        var cashState = CashLedgerProjector.Project(envelopes);
        Assert.True(cashState.Accounts.ContainsKey(userAccountId));
        Assert.False(cashState.Accounts[userAccountId].IsArchived);
    }

    [Fact]
    public async Task SetupState_Projection_IsDeterministic()
    {
        var handler = new CompleteInitialSetupHandler(_eventStore);
        await handler.HandleAsync("USD", 7, true, true, false, _actorUserId, _deviceId, CancellationToken.None);

        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);

        var state1 = SetupProjector.Project(envelopes);
        var state2 = SetupProjector.Project(envelopes);

        Assert.Equal(state1.IsInitialSetupCompleted, state2.IsInitialSetupCompleted);
        Assert.Equal(state1.ReportingCurrencyCode, state2.ReportingCurrencyCode);
        Assert.Equal(state1.FiscalYearStartMonth, state2.FiscalYearStartMonth);
        Assert.Equal(state1.SetupId, state2.SetupId);
        Assert.Equal(state1.CompletedOn, state2.CompletedOn);
    }

    [Fact]
    public async Task Dashboard_GettingStarted_ShowsWhenEmpty()
    {
        var dashboardHandler = new GetPortfolioDashboardHandler(_eventStore);
        var setupHandler = new GetSetupStateHandler(_eventStore);

        var dashboardVm = new DashboardViewModel(
            dashboardHandler,
            setupStateHandler: setupHandler);

        await dashboardVm.RefreshAsync();

        // No setup completed + no obligations = getting started visible
        Assert.True(dashboardVm.ShowGettingStarted);
    }

    private SeedDemoDataHandler BuildSeedDemoHandler()
    {
        var createAccount = new CreateAccountHandler(_eventStore);
        var createCategory = new CreateCategoryHandler(_eventStore);
        var defaultAccounts = new CreateDefaultAccountsHandler(_eventStore, createAccount);
        var defaultCategories = new CreateDefaultCategoriesHandler(_eventStore, createCategory);
        var createGoal = new CreateFinancialGoalHandler(_eventStore);
        var recordContrib = new RecordGoalContributionHandler(_eventStore);
        var retProfile = new DefineRetirementProfileHandler(_eventStore);
        var retAssumptions = new SetRetirementAssumptionsHandler(_eventStore);
        var defineBudget = new DefineBudgetHandler(_eventStore);
        var createRecurring = new CreateRecurringHandler(_eventStore);
        var postRecurring = new PostRecurringNowHandler(_eventStore);

        return new SeedDemoDataHandler(
            _eventStore, defaultAccounts, defaultCategories,
            createAccount, createCategory,
            createGoal, recordContrib,
            retProfile, retAssumptions,
            defineBudget, createRecurring, postRecurring);
    }

    private ClearDemoDataHandler BuildClearDemoHandler()
    {
        return new ClearDemoDataHandler(
            _eventStore,
            new ArchiveAccountHandler(_eventStore),
            new ArchiveCategoryHandler(_eventStore),
            new ArchiveFinancialGoalHandler(_eventStore),
            new ArchiveRetirementAssumptionsHandler(_eventStore),
            new ArchiveBudgetHandler(_eventStore),
            new ArchiveRecurringHandler(_eventStore),
            new ArchiveAssetHandler(_eventStore),
            new ArchiveInvestmentAccountHandler(_eventStore));
    }
}
