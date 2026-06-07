using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Stable StreamId for setup events ---
public static class SetupConstants
{
    /// <summary>
    /// Constant GUID used as the StreamId for all setup/demo events.
    /// </summary>
    public static readonly Guid SetupStreamGuid = new("00000000-0000-0000-0000-5E70F1D00000");
}

// --- DTOs ---

public sealed record SetupStateDto(
    bool IsInitialSetupCompleted,
    bool IsDemoModeActive,
    DateOnly? CompletedOn,
    Guid? SetupId,
    Guid? DemoSeedId,
    string ReportingCurrencyCode,
    int FiscalYearStartMonth,
    bool CreatedDefaultAccounts,
    bool CreatedDefaultCategories,
    bool SeededDemoData);

public sealed record DemoManifest(
    Guid DemoSeedId,
    List<Guid> AccountIds,
    List<Guid> CategoryIds,
    List<Guid> ObligationIds,
    List<Guid> GoalIds,
    List<Guid> ContributionIds,
    List<Guid> BudgetIds,
    List<Guid> RecurringIds,
    List<Guid> AssetIds,
    List<Guid> InvestmentAccountIds,
    List<Guid> RetirementProfileIds,
    List<Guid> RetirementAssumptionIds,
    List<Guid> IncomeEventIds,
    List<Guid> ExpenseEventIds);

// --- Handlers ---

public sealed class GetSetupStateHandler
{
    private readonly IEventStore _store;
    public GetSetupStateHandler(IEventStore store) => _store = store;

    public async Task<SetupStateDto> HandleAsync(CancellationToken ct)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = SetupProjector.Project(envelopes);
        return new SetupStateDto(
            state.IsInitialSetupCompleted,
            state.IsDemoModeActive,
            state.CompletedOn,
            state.SetupId,
            state.DemoSeedId,
            state.ReportingCurrencyCode,
            state.FiscalYearStartMonth,
            state.CreatedDefaultAccounts,
            state.CreatedDefaultCategories,
            state.SeededDemoData);
    }
}

public sealed class CompleteInitialSetupHandler
{
    private readonly IEventStore _store;
    public CompleteInitialSetupHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(
        string reportingCurrencyCode, int fiscalYearStartMonth,
        bool createdDefaultAccounts, bool createdDefaultCategories, bool seededDemoData,
        Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var setupId = Guid.NewGuid();
        var ev = new InitialSetupCompleted(
            DateOnly.FromDateTime(DateTime.Today), setupId,
            reportingCurrencyCode, fiscalYearStartMonth,
            createdDefaultAccounts, createdDefaultCategories, seededDemoData);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(SetupConstants.SetupStreamGuid),
            nameof(InitialSetupCompleted), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return setupId;
    }
}

public sealed class CreateDefaultAccountsHandler
{
    private readonly IEventStore _store;
    private readonly CreateAccountHandler _createAccount;
    public CreateDefaultAccountsHandler(IEventStore store, CreateAccountHandler createAccount)
    {
        _store = store;
        _createAccount = createAccount;
    }

    public async Task<List<Guid>> HandleAsync(string currencyCode, Guid setupId, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        // Idempotency check
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = SetupProjector.Project(envelopes);
        if (state.CreatedDefaultAccounts)
            return [];

        var today = DateOnly.FromDateTime(DateTime.Today);
        var ids = new List<Guid>();

        // Append marker event
        var ev = new DefaultAccountsCreated(today, setupId, currencyCode);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(SetupConstants.SetupStreamGuid),
            nameof(DefaultAccountsCreated), DateTimeOffset.UtcNow, today,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);

        // Create 3 default accounts
        var cashId = await _createAccount.HandleAsync(
            new CreateAccountCommand(null, "Cash", "Cash", 0m, currencyCode, today),
            actorUserId, deviceId, ct);
        ids.Add(cashId);

        var bankId = await _createAccount.HandleAsync(
            new CreateAccountCommand(null, "Bank", "Bank", 0m, currencyCode, today),
            actorUserId, deviceId, ct);
        ids.Add(bankId);

        var savingsId = await _createAccount.HandleAsync(
            new CreateAccountCommand(null, "Savings", "Savings", 0m, currencyCode, today),
            actorUserId, deviceId, ct);
        ids.Add(savingsId);

        return ids;
    }
}

public sealed class CreateDefaultCategoriesHandler
{
    private readonly IEventStore _store;
    private readonly CreateCategoryHandler _createCategory;
    public CreateDefaultCategoriesHandler(IEventStore store, CreateCategoryHandler createCategory)
    {
        _store = store;
        _createCategory = createCategory;
    }

    public async Task<List<Guid>> HandleAsync(Guid setupId, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = SetupProjector.Project(envelopes);
        if (state.CreatedDefaultCategories)
            return [];

        var today = DateOnly.FromDateTime(DateTime.Today);
        var ids = new List<Guid>();

        var ev = new DefaultCategoriesCreated(today, setupId);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(SetupConstants.SetupStreamGuid),
            nameof(DefaultCategoriesCreated), DateTimeOffset.UtcNow, today,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);

        // Income categories
        ids.Add(await _createCategory.HandleAsync(new CreateCategoryCommand(null, "Salary", "income", null), actorUserId, deviceId, ct));
        ids.Add(await _createCategory.HandleAsync(new CreateCategoryCommand(null, "Freelance", "income", null), actorUserId, deviceId, ct));
        ids.Add(await _createCategory.HandleAsync(new CreateCategoryCommand(null, "Investment Returns", "income", null), actorUserId, deviceId, ct));
        ids.Add(await _createCategory.HandleAsync(new CreateCategoryCommand(null, "Other Income", "income", null), actorUserId, deviceId, ct));

        // Expense categories
        ids.Add(await _createCategory.HandleAsync(new CreateCategoryCommand(null, "Food & Groceries", "expense", null), actorUserId, deviceId, ct));
        ids.Add(await _createCategory.HandleAsync(new CreateCategoryCommand(null, "Transportation", "expense", null), actorUserId, deviceId, ct));
        ids.Add(await _createCategory.HandleAsync(new CreateCategoryCommand(null, "Housing & Rent", "expense", null), actorUserId, deviceId, ct));
        ids.Add(await _createCategory.HandleAsync(new CreateCategoryCommand(null, "Utilities", "expense", null), actorUserId, deviceId, ct));
        ids.Add(await _createCategory.HandleAsync(new CreateCategoryCommand(null, "Healthcare", "expense", null), actorUserId, deviceId, ct));
        ids.Add(await _createCategory.HandleAsync(new CreateCategoryCommand(null, "Entertainment", "expense", null), actorUserId, deviceId, ct));
        ids.Add(await _createCategory.HandleAsync(new CreateCategoryCommand(null, "Education", "expense", null), actorUserId, deviceId, ct));
        ids.Add(await _createCategory.HandleAsync(new CreateCategoryCommand(null, "Shopping", "expense", null), actorUserId, deviceId, ct));

        return ids;
    }
}

public sealed class SeedDemoDataHandler
{
    private readonly IEventStore _store;
    private readonly CreateDefaultAccountsHandler _defaultAccounts;
    private readonly CreateDefaultCategoriesHandler _defaultCategories;
    private readonly CreateAccountHandler _createAccount;
    private readonly CreateCategoryHandler _createCategory;
    private readonly CreateFinancialGoalHandler _createGoal;
    private readonly RecordGoalContributionHandler _recordContrib;
    private readonly DefineRetirementProfileHandler _retirementProfile;
    private readonly SetRetirementAssumptionsHandler _retirementAssumptions;
    private readonly DefineBudgetHandler _defineBudget;
    private readonly CreateRecurringHandler _createRecurring;
    private readonly PostRecurringNowHandler _postRecurring;

    public SeedDemoDataHandler(
        IEventStore store,
        CreateDefaultAccountsHandler defaultAccounts,
        CreateDefaultCategoriesHandler defaultCategories,
        CreateAccountHandler createAccount,
        CreateCategoryHandler createCategory,
        CreateFinancialGoalHandler createGoal,
        RecordGoalContributionHandler recordContrib,
        DefineRetirementProfileHandler retirementProfile,
        SetRetirementAssumptionsHandler retirementAssumptions,
        DefineBudgetHandler defineBudget,
        CreateRecurringHandler createRecurring,
        PostRecurringNowHandler postRecurring)
    {
        _store = store;
        _defaultAccounts = defaultAccounts;
        _defaultCategories = defaultCategories;
        _createAccount = createAccount;
        _createCategory = createCategory;
        _createGoal = createGoal;
        _recordContrib = recordContrib;
        _retirementProfile = retirementProfile;
        _retirementAssumptions = retirementAssumptions;
        _defineBudget = defineBudget;
        _createRecurring = createRecurring;
        _postRecurring = postRecurring;
    }

    public async Task<DemoManifest> HandleAsync(string currencyCode, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        // Idempotency check
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = SetupProjector.Project(envelopes);
        if (state.IsDemoModeActive && state.DemoSeedId.HasValue)
            return new DemoManifest(state.DemoSeedId.Value, [], [], [], [], [], [], [], [], [], [], [], [], []);

        var demoSeedId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var manifest = new DemoManifest(demoSeedId, [], [], [], [], [], [], [], [], [], [], [], [], []);

        // 1) Append DemoDataSeeded event
        var seedEv = new DemoDataSeeded(today, demoSeedId, "v1", "Demo data seeded via setup wizard");
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(SetupConstants.SetupStreamGuid),
            nameof(DemoDataSeeded), DateTimeOffset.UtcNow, today,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(seedEv, DomainJson.Options)), ct);

        // 2) Create default accounts if not already done
        var setupId = Guid.NewGuid();
        var accountIds = await _defaultAccounts.HandleAsync(currencyCode, setupId, actorUserId, deviceId, ct);
        manifest.AccountIds.AddRange(accountIds);

        // If accounts already existed, load them
        if (accountIds.Count == 0)
        {
            var cashState = CashLedgerProjector.Project(await _store.ReadAllAsync(DateTimeOffset.MinValue, ct));
            accountIds = cashState.Accounts.Keys.Take(3).ToList();
        }

        var primaryAccountId = accountIds.Count > 0 ? accountIds[0] : Guid.NewGuid();
        var bankAccountId = accountIds.Count > 1 ? accountIds[1] : primaryAccountId;
        var savingsAccountId = accountIds.Count > 2 ? accountIds[2] : primaryAccountId;

        // 3) Create default categories if not already done
        var categoryIds = await _defaultCategories.HandleAsync(setupId, actorUserId, deviceId, ct);
        manifest.CategoryIds.AddRange(categoryIds);

        // 4) Record incomes (2)
        var income1Id = Guid.NewGuid();
        manifest.IncomeEventIds.Add(income1Id);
        var incomeEv1 = new IncomeRecorded(primaryAccountId, new Money(15000m, new Currency(currencyCode, 2)),
            today.AddMonths(-1), "Monthly Salary");
        await _store.AppendAsync(new EventEnvelope(
            new EventId(income1Id), new StreamId(primaryAccountId),
            nameof(IncomeRecorded), DateTimeOffset.UtcNow, incomeEv1.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(incomeEv1, DomainJson.Options)), ct);

        var income2Id = Guid.NewGuid();
        manifest.IncomeEventIds.Add(income2Id);
        var incomeEv2 = new IncomeRecorded(primaryAccountId, new Money(15000m, new Currency(currencyCode, 2)),
            today, "Monthly Salary");
        await _store.AppendAsync(new EventEnvelope(
            new EventId(income2Id), new StreamId(primaryAccountId),
            nameof(IncomeRecorded), DateTimeOffset.UtcNow, incomeEv2.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(incomeEv2, DomainJson.Options)), ct);

        // 5) Record expenses (5)
        var expenseCategories = new[] { "Food & Groceries", "Transportation", "Utilities", "Entertainment", "Shopping" };
        var expenseAmounts = new[] { 2500m, 800m, 1200m, 600m, 1500m };
        for (int i = 0; i < 5; i++)
        {
            var expId = Guid.NewGuid();
            manifest.ExpenseEventIds.Add(expId);
            var expEv = new ExpenseRecorded(primaryAccountId, new Money(expenseAmounts[i], new Currency(currencyCode, 2)),
                today.AddDays(-(5 - i)), expenseCategories[i], $"Demo {expenseCategories[i]}");
            await _store.AppendAsync(new EventEnvelope(
                new EventId(expId), new StreamId(primaryAccountId),
                nameof(ExpenseRecorded), DateTimeOffset.UtcNow, expEv.EffectiveDate,
                actorUserId, deviceId, correlationId, null, 1,
                JsonSerializer.Serialize(expEv, DomainJson.Options)), ct);
        }

        // 6) Create obligations (2) - using raw events to avoid needing rule engine
        var loanId = Guid.NewGuid();
        manifest.ObligationIds.Add(loanId);
        var loanEv = new ObligationCreated(loanId, "Personal Loan", "Loan",
            new Money(50000m, new Currency(currencyCode, 2)), today.AddMonths(-6), currencyCode);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(loanId),
            nameof(ObligationCreated), DateTimeOffset.UtcNow, loanEv.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(loanEv, DomainJson.Options)), ct);

        var creditId = Guid.NewGuid();
        manifest.ObligationIds.Add(creditId);
        var creditEv = new ObligationCreated(creditId, "Credit Card Balance", "CreditCard",
            new Money(12000m, new Currency(currencyCode, 2)), today.AddMonths(-3), currencyCode);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(creditId),
            nameof(ObligationCreated), DateTimeOffset.UtcNow, creditEv.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(creditEv, DomainJson.Options)), ct);

        // 7) Define schedules for obligations
        var loanScheduleSpec = JsonSerializer.Serialize(new
        {
            amount = 2500m, currency = currencyCode,
            startDate = today.AddMonths(-5).ToString("yyyy-MM-dd"),
            endDate = today.AddMonths(18).ToString("yyyy-MM-dd"),
            dayOfMonth = 15
        });
        var loanSchedEv = new ScheduleDefined(Guid.NewGuid(), loanId, "MonthlyFixed",
            loanScheduleSpec, "Africa/Cairo", today.AddMonths(-6));
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(loanId),
            "ScheduleDefined", DateTimeOffset.UtcNow, loanSchedEv.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(loanSchedEv, DomainJson.Options)), ct);

        var creditScheduleSpec = JsonSerializer.Serialize(new
        {
            amount = 3000m, currency = currencyCode,
            startDate = today.AddMonths(-2).ToString("yyyy-MM-dd"),
            endDate = today.AddMonths(6).ToString("yyyy-MM-dd"),
            dayOfMonth = 1
        });
        var creditSchedEv = new ScheduleDefined(Guid.NewGuid(), creditId, "MonthlyFixed",
            creditScheduleSpec, "Africa/Cairo", today.AddMonths(-3));
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(creditId),
            "ScheduleDefined", DateTimeOffset.UtcNow, creditSchedEv.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(creditSchedEv, DomainJson.Options)), ct);

        // 8) Record payments (2)
        var pay1Ev = new PaymentMade(loanId, new Money(2500m, new Currency(currencyCode, 2)),
            today.AddMonths(-4), "Loan payment - month 1");
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(loanId),
            nameof(PaymentMade), DateTimeOffset.UtcNow, pay1Ev.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(pay1Ev, DomainJson.Options)), ct);

        var pay2Ev = new PaymentMade(creditId, new Money(3000m, new Currency(currencyCode, 2)),
            today.AddMonths(-1), "Credit card payment");
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(creditId),
            nameof(PaymentMade), DateTimeOffset.UtcNow, pay2Ev.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(pay2Ev, DomainJson.Options)), ct);

        // 9) Create 1 asset with price
        var assetId = Guid.NewGuid();
        manifest.AssetIds.Add(assetId);
        var qtySpec = JsonSerializer.Serialize(new { amount = 5m, symbol = "GLD" });
        var assetEv = new AssetCreated(assetId, "Gold Reserve", "Commodity", currencyCode, qtySpec, [], "Demo gold asset", today.AddMonths(-2));
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(assetId),
            nameof(AssetCreated), DateTimeOffset.UtcNow, assetEv.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(assetEv, DomainJson.Options)), ct);

        var priceEv = new AssetPriceRecorded(Guid.NewGuid(), assetId, today.AddDays(-7), 3500m, currencyCode, "Demo", "Demo price");
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(assetId),
            nameof(AssetPriceRecorded), DateTimeOffset.UtcNow, priceEv.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(priceEv, DomainJson.Options)), ct);

        // 10) Create 1 investment account with 2 transactions
        var invAccId = Guid.NewGuid();
        manifest.InvestmentAccountIds.Add(invAccId);
        var invAccEv = new InvestmentAccountCreated(invAccId, "Demo Brokerage", currencyCode, "DemoBroker", today.AddMonths(-2));
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(invAccId),
            nameof(InvestmentAccountCreated), DateTimeOffset.UtcNow, invAccEv.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(invAccEv, DomainJson.Options)), ct);

        var demoAssetId = Guid.NewGuid();
        var buyEv = new InvestmentTransactionRecorded(Guid.NewGuid(), invAccId, demoAssetId, "AAPL", "Buy", today.AddMonths(-1), null, 10m, 180m, 0m, 0m, currencyCode, null, "Demo buy", "", today.AddMonths(-1));
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(invAccId),
            nameof(InvestmentTransactionRecorded), DateTimeOffset.UtcNow, buyEv.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(buyEv, DomainJson.Options)), ct);

        var divEv = new InvestmentTransactionRecorded(Guid.NewGuid(), invAccId, demoAssetId, "AAPL", "Dividend", today.AddDays(-5), null, 0m, 0m, 0m, 0m, currencyCode, null, "Demo dividend", "", today.AddDays(-5));
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(invAccId),
            nameof(InvestmentTransactionRecorded), DateTimeOffset.UtcNow, divEv.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(divEv, DomainJson.Options)), ct);

        // 11) Create 1 goal + 2 contributions
        var goalId = await _createGoal.HandleAsync(
            new CreateFinancialGoalCommand(null, "Emergency Fund", "EmergencyFund",
                30000m, currencyCode, today.AddYears(1), "Build 6 months expenses", ["safety"],
                today.AddMonths(-2)),
            actorUserId, deviceId, ct);
        manifest.GoalIds.Add(goalId);

        var contrib1Id = await _recordContrib.HandleAsync(
            new RecordGoalContributionCommand(goalId, null, savingsAccountId,
                5000m, currencyCode, today.AddMonths(-1), "Jan contribution"),
            actorUserId, deviceId, ct);
        manifest.ContributionIds.Add(contrib1Id);

        var contrib2Id = await _recordContrib.HandleAsync(
            new RecordGoalContributionCommand(goalId, null, savingsAccountId,
                3000m, currencyCode, today, "Feb contribution"),
            actorUserId, deviceId, ct);
        manifest.ContributionIds.Add(contrib2Id);

        // 12) Retirement profile + assumptions
        var retProfileId = await _retirementProfile.HandleAsync(
            new DefineRetirementProfileCommand(null, "My Retirement Plan",
                today.AddYears(25), 5000m, currencyCode, 85,
                "SafeWithdrawalRate", 0.04m, today),
            actorUserId, deviceId, ct);
        manifest.RetirementProfileIds.Add(retProfileId);

        var retAssumId = await _retirementAssumptions.HandleAsync(
            new SetRetirementAssumptionsCommand(null, "Baseline",
                0.07m, 0.03m, 0.02m, 2000m, currencyCode, currencyCode, today),
            actorUserId, deviceId, ct);
        manifest.RetirementAssumptionIds.Add(retAssumId);

        // 13) Create 1 budget
        var budgetId = await _defineBudget.HandleAsync(
            new DefineBudgetCommand(null, today.Year, today.Month, currencyCode,
                "global", null, null, 10000m, "NeverCarry"),
            actorUserId, deviceId, ct);
        manifest.BudgetIds.Add(budgetId);

        // 14) Create 1 recurring transaction and post once
        var recurringId = await _createRecurring.HandleAsync(
            new CreateRecurringCommand(null, "expense", primaryAccountId,
                250m, currencyCode, null, "Monthly internet subscription", "Internet Bill",
                "Monthly", 1, today, null, false),
            actorUserId, deviceId, ct);
        manifest.RecurringIds.Add(recurringId);

        await _postRecurring.HandleAsync(
            new PostRecurringNowCommand(recurringId, today),
            actorUserId, deviceId, ct);

        return manifest;
    }
}

public sealed class ClearDemoDataHandler
{
    private readonly IEventStore _store;
    private readonly ArchiveAccountHandler _archiveAccount;
    private readonly ArchiveCategoryHandler _archiveCategory;
    private readonly ArchiveFinancialGoalHandler _archiveGoal;
    private readonly ArchiveRetirementAssumptionsHandler _archiveRetAssumptions;
    private readonly ArchiveBudgetHandler _archiveBudget;
    private readonly ArchiveRecurringHandler _archiveRecurring;
    private readonly ArchiveAssetHandler _archiveAsset;
    private readonly ArchiveInvestmentAccountHandler _archiveInvAccount;

    public ClearDemoDataHandler(
        IEventStore store,
        ArchiveAccountHandler archiveAccount,
        ArchiveCategoryHandler archiveCategory,
        ArchiveFinancialGoalHandler archiveGoal,
        ArchiveRetirementAssumptionsHandler archiveRetAssumptions,
        ArchiveBudgetHandler archiveBudget,
        ArchiveRecurringHandler archiveRecurring,
        ArchiveAssetHandler archiveAsset,
        ArchiveInvestmentAccountHandler archiveInvAccount)
    {
        _store = store;
        _archiveAccount = archiveAccount;
        _archiveCategory = archiveCategory;
        _archiveGoal = archiveGoal;
        _archiveRetAssumptions = archiveRetAssumptions;
        _archiveBudget = archiveBudget;
        _archiveRecurring = archiveRecurring;
        _archiveAsset = archiveAsset;
        _archiveInvAccount = archiveInvAccount;
    }

    public async Task HandleAsync(DemoManifest manifest, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        const string reason = "Demo data cleared";

        // Archive accounts
        foreach (var id in manifest.AccountIds)
            await _archiveAccount.HandleAsync(new ArchiveAccountCommand(id, today, reason), actorUserId, deviceId, ct);

        // Archive categories
        foreach (var id in manifest.CategoryIds)
            await _archiveCategory.HandleAsync(new ArchiveCategoryCommand(id, reason), actorUserId, deviceId, ct);

        // Archive goals
        foreach (var id in manifest.GoalIds)
            await _archiveGoal.HandleAsync(new ArchiveFinancialGoalCommand(id, today, reason), actorUserId, deviceId, ct);

        // Archive retirement assumptions
        foreach (var id in manifest.RetirementAssumptionIds)
            await _archiveRetAssumptions.HandleAsync(new ArchiveRetirementAssumptionsCommand(id, today, reason), actorUserId, deviceId, ct);

        // Archive budgets
        foreach (var id in manifest.BudgetIds)
            await _archiveBudget.HandleAsync(new ArchiveBudgetCommand(id, reason), actorUserId, deviceId, ct);

        // Archive recurring
        foreach (var id in manifest.RecurringIds)
            await _archiveRecurring.HandleAsync(new ArchiveRecurringCommand(id, reason), actorUserId, deviceId, ct);

        // Archive assets
        foreach (var id in manifest.AssetIds)
            await _archiveAsset.HandleAsync(new ArchiveAssetCommand(id, today, reason), actorUserId, deviceId, ct);

        // Archive investment accounts
        foreach (var id in manifest.InvestmentAccountIds)
            await _archiveInvAccount.HandleAsync(new ArchiveInvestmentAccountCommand(id, today, reason), actorUserId, deviceId, ct);

        // Reverse income events
        foreach (var id in manifest.IncomeEventIds)
        {
            var revEv = new IncomeReversed(id, Guid.Empty, 0m, today, reason);
            await _store.AppendAsync(new EventEnvelope(
                new EventId(Guid.NewGuid()), new StreamId(SetupConstants.SetupStreamGuid),
                nameof(IncomeReversed), DateTimeOffset.UtcNow, today,
                actorUserId, deviceId, Guid.NewGuid(), null, 1,
                JsonSerializer.Serialize(revEv, DomainJson.Options)), ct);
        }

        // Reverse expense events
        foreach (var id in manifest.ExpenseEventIds)
        {
            var revEv = new ExpenseReversed(id, Guid.Empty, 0m, today, reason);
            await _store.AppendAsync(new EventEnvelope(
                new EventId(Guid.NewGuid()), new StreamId(SetupConstants.SetupStreamGuid),
                nameof(ExpenseReversed), DateTimeOffset.UtcNow, today,
                actorUserId, deviceId, Guid.NewGuid(), null, 1,
                JsonSerializer.Serialize(revEv, DomainJson.Options)), ct);
        }

        // Append DemoDataCleared event
        var ev = new DemoDataCleared(today, manifest.DemoSeedId, reason);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(SetupConstants.SetupStreamGuid),
            nameof(DemoDataCleared), DateTimeOffset.UtcNow, today,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}
