using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Services.Serialization;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class SplitTransactionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public SplitTransactionTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"SplitTxnTests_{id}.db");
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

    private async Task<Guid> CreateAccountAsync(string name = "Wallet", decimal opening = 10000m)
    {
        var handler = new CreateAccountHandler(_eventStore);
        return await handler.HandleAsync(
            new CreateAccountCommand(null, name, "Cash", opening, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    // ================================================================
    // 1) SplitExpense_CreatesN_LedgerRows_WithSameCorrelationId
    // ================================================================
    [Fact]
    public async Task SplitExpense_CreatesN_LedgerRows_WithSameCorrelationId()
    {
        var accountId = await CreateAccountAsync();
        var handler = new RecordSplitExpenseHandler(_eventStore);

        await handler.HandleAsync(new RecordSplitExpenseCommand(
            accountId, new DateOnly(2025, 3, 1), 500m, "EGP",
            new List<SplitLineDto>
            {
                new("Food", 300m, "Groceries"),
                new("Household", 150m, "Cleaning supplies"),
                new("Personal", 50m, null)
            },
            "Supermarket trip"),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledger = CashLedgerProjector.Project(all);

        var splitRows = ledger.Rows
            .Where(r => r.Reference.StartsWith("SplitExpense:"))
            .ToList();

        Assert.Equal(3, splitRows.Count);

        // All share the same CorrelationId
        var correlationIds = splitRows.Select(r => r.CorrelationId).Distinct().ToList();
        Assert.Single(correlationIds);

        // Category matches
        Assert.Contains(splitRows, r => r.Category == "Food" && r.Amount == 300m);
        Assert.Contains(splitRows, r => r.Category == "Household" && r.Amount == 150m);
        Assert.Contains(splitRows, r => r.Category == "Personal" && r.Amount == 50m);

        // All are Out direction
        Assert.All(splitRows, r => Assert.Equal("Out", r.Direction));
    }

    // ================================================================
    // 2) SplitExpense_UpdatesAccountBalance_ByTotalAmount
    // ================================================================
    [Fact]
    public async Task SplitExpense_UpdatesAccountBalance_ByTotalAmount()
    {
        var accountId = await CreateAccountAsync("Checking", 5000m);
        var handler = new RecordSplitExpenseHandler(_eventStore);

        await handler.HandleAsync(new RecordSplitExpenseCommand(
            accountId, new DateOnly(2025, 3, 1), 1200m, "EGP",
            new List<SplitLineDto>
            {
                new("Food", 700m, null),
                new("Transport", 500m, null)
            },
            null),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledger = CashLedgerProjector.Project(all);

        Assert.Equal(3800m, ledger.Accounts[accountId].Balance); // 5000 - 1200
    }

    // ================================================================
    // 3) SplitExpense_BudgetUtilization_SumsPerCategoryCorrectly
    // ================================================================
    [Fact]
    public async Task SplitExpense_BudgetUtilization_SumsPerCategoryCorrectly()
    {
        var accountId = await CreateAccountAsync("Wallet", 10000m);

        // Create categories
        var foodCatId = Guid.NewGuid();
        var transportCatId = Guid.NewGuid();
        await AppendEvent(new CategoryCreated(foodCatId, "Food", "expense", null, new DateOnly(2025, 1, 1)));
        await AppendEvent(new CategoryCreated(transportCatId, "Transport", "expense", null, new DateOnly(2025, 1, 1)));

        // Create budgets for March 2025
        await AppendBudget(2025, 3, "EGP", "category", foodCatId, null, 2000m);
        await AppendBudget(2025, 3, "EGP", "category", transportCatId, null, 1000m);

        // Record split expense
        var handler = new RecordSplitExpenseHandler(_eventStore);
        await handler.HandleAsync(new RecordSplitExpenseCommand(
            accountId, new DateOnly(2025, 3, 15), 900m, "EGP",
            new List<SplitLineDto>
            {
                new("Food", 600m, null),
                new("Transport", 300m, null)
            },
            null),
            _actorUserId, _deviceId, CancellationToken.None);

        // Also record a regular expense to Food
        await AppendExpenseEvent(accountId, 200m, "EGP", "Food", "Lunch", new DateOnly(2025, 3, 10));

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledger = CashLedgerProjector.Project(all);
        var budgetState = BudgetProjector.Project(all);
        var catState = CategoryProjector.Project(all);

        var utilization = BudgetProjector.ComputeUtilization(budgetState, ledger, catState, 2025, 3);

        var foodBudget = utilization.Single(u => u.ScopeLabel == "Food");
        var transportBudget = utilization.Single(u => u.ScopeLabel == "Transport");

        // Food: 600 (split) + 200 (regular) = 800
        Assert.Equal(800m, foodBudget.ActualAmount);
        // Transport: 300 (split)
        Assert.Equal(300m, transportBudget.ActualAmount);
    }

    // ================================================================
    // 4) SplitExpense_CategoryTotals_AccumulateCorrectly
    // ================================================================
    [Fact]
    public async Task SplitExpense_CategoryTotals_AccumulateCorrectly()
    {
        var accountId = await CreateAccountAsync("Wallet", 10000m);
        var handler = new RecordSplitExpenseHandler(_eventStore);

        // Two split expenses
        await handler.HandleAsync(new RecordSplitExpenseCommand(
            accountId, new DateOnly(2025, 3, 1), 500m, "EGP",
            new List<SplitLineDto> { new("Food", 300m, null), new("Transport", 200m, null) },
            null), _actorUserId, _deviceId, CancellationToken.None);

        await handler.HandleAsync(new RecordSplitExpenseCommand(
            accountId, new DateOnly(2025, 3, 5), 400m, "EGP",
            new List<SplitLineDto> { new("Food", 150m, null), new("Utilities", 250m, null) },
            null), _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledger = CashLedgerProjector.Project(all);

        var foodTotal = ledger.Rows.Where(r => r.Category == "Food" && r.Direction == "Out").Sum(r => r.Amount);
        var transportTotal = ledger.Rows.Where(r => r.Category == "Transport" && r.Direction == "Out").Sum(r => r.Amount);
        var utilitiesTotal = ledger.Rows.Where(r => r.Category == "Utilities" && r.Direction == "Out").Sum(r => r.Amount);

        Assert.Equal(450m, foodTotal);      // 300 + 150
        Assert.Equal(200m, transportTotal);  // 200
        Assert.Equal(250m, utilitiesTotal);  // 250
    }

    // ================================================================
    // 5) SplitIncome_CreatesN_LedgerRows_AndUpdatesBalance
    // ================================================================
    [Fact]
    public async Task SplitIncome_CreatesN_LedgerRows_AndUpdatesBalance()
    {
        var accountId = await CreateAccountAsync("Checking", 5000m);
        var handler = new RecordSplitIncomeHandler(_eventStore);

        await handler.HandleAsync(new RecordSplitIncomeCommand(
            accountId, new DateOnly(2025, 3, 1), 8000m, "EGP",
            new List<IncomeSplitLineDto>
            {
                new("Salary", 5000m),
                new("Freelance", 3000m)
            },
            "March income"),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledger = CashLedgerProjector.Project(all);

        // Balance: 5000 + 8000 = 13000
        Assert.Equal(13000m, ledger.Accounts[accountId].Balance);

        var splitRows = ledger.Rows
            .Where(r => r.Reference.StartsWith("SplitIncome:"))
            .ToList();

        Assert.Equal(2, splitRows.Count);
        Assert.All(splitRows, r => Assert.Equal("In", r.Direction));
        Assert.Contains(splitRows, r => r.Reference.Contains("[Salary]") && r.Amount == 5000m);
        Assert.Contains(splitRows, r => r.Reference.Contains("[Freelance]") && r.Amount == 3000m);

        // All share the same CorrelationId
        var correlationIds = splitRows.Select(r => r.CorrelationId).Distinct().ToList();
        Assert.Single(correlationIds);
    }

    // ================================================================
    // 6) SplitValidation_RejectsIfSumNotEqualTotal
    // ================================================================
    [Fact]
    public async Task SplitValidation_RejectsIfSumNotEqualTotal()
    {
        var accountId = await CreateAccountAsync();
        var handler = new RecordSplitExpenseHandler(_eventStore);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new RecordSplitExpenseCommand(
                accountId, new DateOnly(2025, 3, 1), 500m, "EGP",
                new List<SplitLineDto>
                {
                    new("Food", 300m, null),
                    new("Transport", 100m, null) // Sum = 400, not 500
                },
                null), _actorUserId, _deviceId, CancellationToken.None));

        Assert.Contains("does not equal TotalAmount", ex.Message);
    }

    // ================================================================
    // 7) SplitValidation_RejectsIfLessThanTwoLines
    // ================================================================
    [Fact]
    public async Task SplitValidation_RejectsIfLessThanTwoLines()
    {
        var accountId = await CreateAccountAsync();
        var expenseHandler = new RecordSplitExpenseHandler(_eventStore);
        var incomeHandler = new RecordSplitIncomeHandler(_eventStore);

        // Single line expense
        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            expenseHandler.HandleAsync(new RecordSplitExpenseCommand(
                accountId, new DateOnly(2025, 3, 1), 500m, "EGP",
                new List<SplitLineDto> { new("Food", 500m, null) },
                null), _actorUserId, _deviceId, CancellationToken.None));

        Assert.Contains("at least 2 lines", ex1.Message);

        // Single line income
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            incomeHandler.HandleAsync(new RecordSplitIncomeCommand(
                accountId, new DateOnly(2025, 3, 1), 500m, "EGP",
                new List<IncomeSplitLineDto> { new("Salary", 500m) },
                null), _actorUserId, _deviceId, CancellationToken.None));

        Assert.Contains("at least 2 lines", ex2.Message);

        // Empty lines
        var ex3 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            expenseHandler.HandleAsync(new RecordSplitExpenseCommand(
                accountId, new DateOnly(2025, 3, 1), 500m, "EGP",
                new List<SplitLineDto>(),
                null), _actorUserId, _deviceId, CancellationToken.None));

        Assert.Contains("at least 2 lines", ex3.Message);
    }

    // ================================================================
    // 8) Determinism_ReplaySameEvents_ProducesSameLedgerState
    // ================================================================
    [Fact]
    public async Task Determinism_ReplaySameEvents_ProducesSameLedgerState()
    {
        var accountId = await CreateAccountAsync("Wallet", 10000m);

        var handler = new RecordSplitExpenseHandler(_eventStore);
        await handler.HandleAsync(new RecordSplitExpenseCommand(
            accountId, new DateOnly(2025, 3, 1), 500m, "EGP",
            new List<SplitLineDto> { new("Food", 300m, null), new("Transport", 200m, null) },
            "Trip"), _actorUserId, _deviceId, CancellationToken.None);

        var incomeHandler = new RecordSplitIncomeHandler(_eventStore);
        await incomeHandler.HandleAsync(new RecordSplitIncomeCommand(
            accountId, new DateOnly(2025, 3, 2), 2000m, "EGP",
            new List<IncomeSplitLineDto> { new("Salary", 1500m), new("Bonus", 500m) },
            null), _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var envelopes = all.ToList();

        // Project twice from the same envelopes
        var state1 = CashLedgerProjector.Project(envelopes);
        var state2 = CashLedgerProjector.Project(envelopes);

        Assert.Equal(state1.Accounts[accountId].Balance, state2.Accounts[accountId].Balance);
        Assert.Equal(state1.Rows.Count, state2.Rows.Count);
        Assert.Equal(state1.TotalIncome, state2.TotalIncome);
        Assert.Equal(state1.TotalExpense, state2.TotalExpense);

        for (int i = 0; i < state1.Rows.Count; i++)
        {
            Assert.Equal(state1.Rows[i].Category, state2.Rows[i].Category);
            Assert.Equal(state1.Rows[i].Amount, state2.Rows[i].Amount);
            Assert.Equal(state1.Rows[i].Direction, state2.Rows[i].Direction);
            Assert.Equal(state1.Rows[i].CorrelationId, state2.Rows[i].CorrelationId);
        }
    }

    // ================================================================
    // 9) ImportSplit_BeforeApply_WritesSplitExpenseRecorded_NotExpenseRecorded
    // ================================================================
    [Fact]
    public async Task ImportSplit_BeforeApply_WritesSplitExpenseRecorded_NotExpenseRecorded()
    {
        // This test simulates what the import UI would do:
        // instead of calling RecordExpenseHandler, it calls RecordSplitExpenseHandler
        // for an imported row that the user chose to split.

        var accountId = await CreateAccountAsync("Checking", 5000m);
        var splitHandler = new RecordSplitExpenseHandler(_eventStore);

        // Simulating a bank import row of 300 EGP that user splits into Food + Transport
        await splitHandler.HandleAsync(new RecordSplitExpenseCommand(
            accountId, new DateOnly(2025, 3, 15), 300m, "EGP",
            new List<SplitLineDto>
            {
                new("Food", 200m, "Imported row split"),
                new("Transport", 100m, "Imported row split")
            },
            "Import: Bank CSV Row #1"),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);

        // Verify: SplitExpenseRecorded event exists, NOT ExpenseRecorded
        var splitEvents = all.Where(e => e.EventType == nameof(SplitExpenseRecorded)).ToList();
        var singleEvents = all.Where(e => e.EventType == nameof(ExpenseRecorded)).ToList();

        Assert.Single(splitEvents);
        Assert.Empty(singleEvents);

        // Verify ledger
        var ledger = CashLedgerProjector.Project(all);
        Assert.Equal(4700m, ledger.Accounts[accountId].Balance); // 5000 - 300

        var splitRows = ledger.Rows.Where(r => r.Reference.StartsWith("SplitExpense:")).ToList();
        Assert.Equal(2, splitRows.Count);
        Assert.Contains(splitRows, r => r.Category == "Food" && r.Amount == 200m);
        Assert.Contains(splitRows, r => r.Category == "Transport" && r.Amount == 100m);
    }

    // ================================================================
    // 10) SplitNotes_PreservesParentAndLineNotes
    // ================================================================
    [Fact]
    public async Task SplitNotes_PreservesParentAndLineNotes()
    {
        var accountId = await CreateAccountAsync();
        var handler = new RecordSplitExpenseHandler(_eventStore);

        await handler.HandleAsync(new RecordSplitExpenseCommand(
            accountId, new DateOnly(2025, 3, 1), 500m, "EGP",
            new List<SplitLineDto>
            {
                new("Food", 300m, "Line note for food"),
                new("Transport", 200m, null) // No line note
            },
            "Parent note"),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledger = CashLedgerProjector.Project(all);

        var splitRows = ledger.Rows.Where(r => r.Reference.StartsWith("SplitExpense:")).ToList();
        Assert.Equal(2, splitRows.Count);

        var foodRow = splitRows.Single(r => r.Category == "Food");
        var transportRow = splitRows.Single(r => r.Category == "Transport");

        // Food row should have both parent and line notes
        Assert.Contains("Parent note", foodRow.Notes);
        Assert.Contains("Line note for food", foodRow.Notes);

        // Transport row should have parent note only
        Assert.Contains("Parent note", transportRow.Notes);
    }

    // ================================================================
    // 11) SplitExpense_Validation_RejectsEmptyCategory
    // ================================================================
    [Fact]
    public async Task SplitExpense_Validation_RejectsEmptyCategory()
    {
        var accountId = await CreateAccountAsync();
        var handler = new RecordSplitExpenseHandler(_eventStore);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new RecordSplitExpenseCommand(
                accountId, new DateOnly(2025, 3, 1), 500m, "EGP",
                new List<SplitLineDto>
                {
                    new("Food", 300m, null),
                    new("", 200m, null) // Empty category
                },
                null), _actorUserId, _deviceId, CancellationToken.None));

        Assert.Contains("empty category", ex.Message);
    }

    // ================================================================
    // 12) SplitExpense_Validation_RejectsArchivedAccount
    // ================================================================
    [Fact]
    public async Task SplitExpense_Validation_RejectsArchivedAccount()
    {
        var accountId = await CreateAccountAsync("Old", 5000m);

        // Archive the account
        var archiveHandler = new ArchiveAccountHandler(_eventStore);
        await archiveHandler.HandleAsync(
            new ArchiveAccountCommand(accountId, new DateOnly(2025, 2, 1), "No longer needed"),
            _actorUserId, _deviceId, CancellationToken.None);

        var handler = new RecordSplitExpenseHandler(_eventStore);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new RecordSplitExpenseCommand(
                accountId, new DateOnly(2025, 3, 1), 500m, "EGP",
                new List<SplitLineDto>
                {
                    new("Food", 300m, null),
                    new("Transport", 200m, null)
                },
                null), _actorUserId, _deviceId, CancellationToken.None));

        Assert.Contains("archived", ex.Message);
    }

    // ================================================================
    // 13) CashLedgerVm_SubmitSplitExpense_WritesSplitExpenseRecorded
    //     (Simulates desktop VM path: handler invoked same way VM does)
    // ================================================================
    [Fact]
    public async Task CashLedgerVm_SubmitSplitExpense_WritesSplitExpenseRecorded()
    {
        var accountId = await CreateAccountAsync("Wallet", 10000m);
        var handler = new RecordSplitExpenseHandler(_eventStore);

        // Simulate what CashLedgerViewModel.SubmitSplitExpenseAsync does:
        var lines = new List<SplitLineDto>
        {
            new("Food", 400m, "Groceries"),
            new("Transport", 100m, null)
        };
        var total = lines.Sum(l => l.Amount);

        await handler.HandleAsync(new RecordSplitExpenseCommand(
            accountId, DateOnly.FromDateTime(DateTime.Today), total, "EGP", lines, "VM submit"),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);

        // Verify SplitExpenseRecorded was written
        var splitEvents = all.Where(e => e.EventType == nameof(SplitExpenseRecorded)).ToList();
        Assert.Single(splitEvents);

        // Verify no plain ExpenseRecorded
        var plainEvents = all.Where(e => e.EventType == nameof(ExpenseRecorded)).ToList();
        Assert.Empty(plainEvents);

        // Verify ledger
        var ledger = CashLedgerProjector.Project(all);
        Assert.Equal(9500m, ledger.Accounts[accountId].Balance); // 10000 - 500
    }

    // ================================================================
    // 14) CashLedgerVm_SubmitSplitIncome_WritesSplitIncomeRecorded
    // ================================================================
    [Fact]
    public async Task CashLedgerVm_SubmitSplitIncome_WritesSplitIncomeRecorded()
    {
        var accountId = await CreateAccountAsync("Checking", 5000m);
        var handler = new RecordSplitIncomeHandler(_eventStore);

        var lines = new List<IncomeSplitLineDto>
        {
            new("Salary", 4000m),
            new("Bonus", 1000m)
        };
        var total = lines.Sum(l => l.Amount);

        await handler.HandleAsync(new RecordSplitIncomeCommand(
            accountId, DateOnly.FromDateTime(DateTime.Today), total, "EGP", lines, "VM submit"),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);

        var splitEvents = all.Where(e => e.EventType == nameof(SplitIncomeRecorded)).ToList();
        Assert.Single(splitEvents);

        var ledger = CashLedgerProjector.Project(all);
        Assert.Equal(10000m, ledger.Accounts[accountId].Balance); // 5000 + 5000
    }

    // ================================================================
    // 15) ImportVm_SplitBeforeApply_WritesSplitExpenseRecorded_AndDoesNotWriteExpenseRecorded
    //     (Simulates ImportViewModel.ApplySplitAsync flow)
    // ================================================================
    [Fact]
    public async Task ImportVm_SplitBeforeApply_WritesSplitExpenseRecorded_AndDoesNotWriteExpenseRecorded()
    {
        var accountId = await CreateAccountAsync("Bank", 8000m);
        var splitHandler = new RecordSplitExpenseHandler(_eventStore);

        // Simulate import VM: user imported a 600 EGP row and chose to split
        var importedAmount = 600m;
        var importedDate = new DateOnly(2025, 4, 10);

        var lines = new List<SplitLineDto>
        {
            new("Utilities", 350m, "Electric"),
            new("Internet", 250m, "ISP bill")
        };
        var total = lines.Sum(l => l.Amount);
        Assert.Equal(importedAmount, total); // VM enforces this

        await splitHandler.HandleAsync(new RecordSplitExpenseCommand(
            accountId, importedDate, total, "EGP", lines, "Import split: Electric + ISP"),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);

        // Only SplitExpenseRecorded, no ExpenseRecorded
        Assert.Single(all.Where(e => e.EventType == nameof(SplitExpenseRecorded)));
        Assert.Empty(all.Where(e => e.EventType == nameof(ExpenseRecorded)));

        // Ledger correct
        var ledger = CashLedgerProjector.Project(all);
        Assert.Equal(7400m, ledger.Accounts[accountId].Balance); // 8000 - 600
        var splitRows = ledger.Rows.Where(r => r.Reference.StartsWith("SplitExpense:")).ToList();
        Assert.Equal(2, splitRows.Count);
    }

    // ================================================================
    // 16) ImportVm_SplitValidation_BlocksApply_WhenSumMismatch
    // ================================================================
    [Fact]
    public async Task ImportVm_SplitValidation_BlocksApply_WhenSumMismatch()
    {
        var accountId = await CreateAccountAsync("Bank", 8000m);
        var splitHandler = new RecordSplitExpenseHandler(_eventStore);

        // Imported row is 600 EGP, but lines sum to 500
        var lines = new List<SplitLineDto>
        {
            new("Utilities", 300m, null),
            new("Internet", 200m, null) // sum = 500, not 600
        };

        // The handler itself validates sum==total, so pass 600 as TotalAmount
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            splitHandler.HandleAsync(new RecordSplitExpenseCommand(
                accountId, new DateOnly(2025, 4, 10), 600m, "EGP", lines, null),
                _actorUserId, _deviceId, CancellationToken.None));

        Assert.Contains("does not equal TotalAmount", ex.Message);

        // Also test: if the VM computes total from lines (500) and it doesn't match imported amount (600),
        // that's a UI-level check. But the domain handler also rejects if sum(lines) != TotalAmount.
        // No events should have been written
        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        Assert.Empty(all.Where(e => e.EventType == nameof(SplitExpenseRecorded)));
    }

    // ================================================================
    // 17) SplitExpense_Reversal_RestoresBalance_AndNegatesRows
    // ================================================================
    [Fact]
    public async Task SplitExpense_Reversal_RestoresBalance_AndNegatesRows()
    {
        var accountId = await CreateAccountAsync("Wallet", 10000m);
        var recordHandler = new RecordSplitExpenseHandler(_eventStore);

        await recordHandler.HandleAsync(new RecordSplitExpenseCommand(
            accountId, new DateOnly(2025, 3, 1), 500m, "EGP",
            new List<SplitLineDto>
            {
                new("Food", 300m, "Groceries"),
                new("Transport", 200m, null)
            },
            "Supermarket"), _actorUserId, _deviceId, CancellationToken.None);

        // Verify balance after expense
        var allAfterExpense = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledgerAfterExpense = CashLedgerProjector.Project(allAfterExpense);
        Assert.Equal(9500m, ledgerAfterExpense.Accounts[accountId].Balance); // 10000 - 500

        // Find the ParentId from the written SplitExpenseRecorded event
        var splitEnvelope = allAfterExpense.First(e => e.EventType == nameof(SplitExpenseRecorded));
        var splitEvent = System.Text.Json.JsonSerializer.Deserialize<SplitExpenseRecorded>(
            splitEnvelope.PayloadJson, DomainJson.Options)!;
        var parentId = splitEvent.ParentId;

        // Reverse it
        var reverseHandler = new ReverseSplitExpenseHandler(_eventStore);
        await reverseHandler.HandleAsync(new ReverseSplitExpenseCommand(
            parentId, "Incorrect purchase", new DateOnly(2025, 3, 2)),
            _actorUserId, _deviceId, CancellationToken.None);

        var allAfterReversal = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledgerAfterReversal = CashLedgerProjector.Project(allAfterReversal);

        // Balance restored
        Assert.Equal(10000m, ledgerAfterReversal.Accounts[accountId].Balance);

        // TotalExpense should be net zero for the split
        // (The TotalExpense was 500 from the split, then -500 from the reversal)
        var reversalRows = ledgerAfterReversal.Rows
            .Where(r => r.Reference.StartsWith("SplitExpenseReversal:"))
            .ToList();

        // Should have 2 reversal rows (one per original line)
        Assert.Equal(2, reversalRows.Count);
        Assert.All(reversalRows, r => Assert.True(r.Amount < 0)); // Negative amounts

        var foodReversal = reversalRows.Single(r => r.Category.StartsWith("Food"));
        var transportReversal = reversalRows.Single(r => r.Category.StartsWith("Transport"));
        Assert.Equal(-300m, foodReversal.Amount);
        Assert.Equal(-200m, transportReversal.Amount);
    }

    // ================================================================
    // 18) SplitIncome_Reversal_RestoresBalance
    // ================================================================
    [Fact]
    public async Task SplitIncome_Reversal_RestoresBalance()
    {
        var accountId = await CreateAccountAsync("Checking", 5000m);
        var recordHandler = new RecordSplitIncomeHandler(_eventStore);

        await recordHandler.HandleAsync(new RecordSplitIncomeCommand(
            accountId, new DateOnly(2025, 3, 1), 8000m, "EGP",
            new List<IncomeSplitLineDto>
            {
                new("Salary", 5000m),
                new("Freelance", 3000m)
            },
            "March income"), _actorUserId, _deviceId, CancellationToken.None);

        // Balance after income
        var allAfterIncome = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledgerAfterIncome = CashLedgerProjector.Project(allAfterIncome);
        Assert.Equal(13000m, ledgerAfterIncome.Accounts[accountId].Balance);

        // Find ParentId
        var incEnv = allAfterIncome.First(e => e.EventType == nameof(SplitIncomeRecorded));
        var incEvent = System.Text.Json.JsonSerializer.Deserialize<SplitIncomeRecorded>(
            incEnv.PayloadJson, DomainJson.Options)!;

        // Reverse it
        var reverseHandler = new ReverseSplitIncomeHandler(_eventStore);
        await reverseHandler.HandleAsync(new ReverseSplitIncomeCommand(
            incEvent.ParentId, "Duplicate entry", new DateOnly(2025, 3, 2)),
            _actorUserId, _deviceId, CancellationToken.None);

        var allAfterReversal = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledgerAfterReversal = CashLedgerProjector.Project(allAfterReversal);

        // Balance restored
        Assert.Equal(5000m, ledgerAfterReversal.Accounts[accountId].Balance);

        var reversalRows = ledgerAfterReversal.Rows
            .Where(r => r.Reference.StartsWith("SplitIncomeReversal:"))
            .ToList();
        Assert.Equal(2, reversalRows.Count);
        Assert.All(reversalRows, r => Assert.True(r.Amount < 0));
    }

    // ================================================================
    // 19) Determinism_WithReversals_SameEvents_ReplaySameState
    // ================================================================
    [Fact]
    public async Task Determinism_WithReversals_SameEvents_ReplaySameState()
    {
        var accountId = await CreateAccountAsync("Wallet", 10000m);

        var expHandler = new RecordSplitExpenseHandler(_eventStore);
        await expHandler.HandleAsync(new RecordSplitExpenseCommand(
            accountId, new DateOnly(2025, 3, 1), 500m, "EGP",
            new List<SplitLineDto> { new("Food", 300m, null), new("Transport", 200m, null) },
            null), _actorUserId, _deviceId, CancellationToken.None);

        // Get ParentId for reversal
        var allBeforeRev = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var splitEnv = allBeforeRev.First(e => e.EventType == nameof(SplitExpenseRecorded));
        var splitEv = System.Text.Json.JsonSerializer.Deserialize<SplitExpenseRecorded>(
            splitEnv.PayloadJson, DomainJson.Options)!;

        var revHandler = new ReverseSplitExpenseHandler(_eventStore);
        await revHandler.HandleAsync(new ReverseSplitExpenseCommand(
            splitEv.ParentId, "Error", new DateOnly(2025, 3, 2)),
            _actorUserId, _deviceId, CancellationToken.None);

        var incHandler = new RecordSplitIncomeHandler(_eventStore);
        await incHandler.HandleAsync(new RecordSplitIncomeCommand(
            accountId, new DateOnly(2025, 3, 3), 2000m, "EGP",
            new List<IncomeSplitLineDto> { new("Salary", 1500m), new("Bonus", 500m) },
            null), _actorUserId, _deviceId, CancellationToken.None);

        var allEnvelopes = (await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None)).ToList();

        var state1 = CashLedgerProjector.Project(allEnvelopes);
        var state2 = CashLedgerProjector.Project(allEnvelopes);

        Assert.Equal(state1.Accounts[accountId].Balance, state2.Accounts[accountId].Balance);
        Assert.Equal(state1.Rows.Count, state2.Rows.Count);
        Assert.Equal(state1.TotalIncome, state2.TotalIncome);
        Assert.Equal(state1.TotalExpense, state2.TotalExpense);

        // Balance: 10000 - 500 + 500 (reversed) + 2000 = 12000
        Assert.Equal(12000m, state1.Accounts[accountId].Balance);
    }

    // ================================================================
    // 20) SplitExpenseReversed_EventIs_Deserializable
    // ================================================================
    [Fact]
    public async Task SplitExpenseReversed_EventIs_Deserializable()
    {
        var accountId = await CreateAccountAsync("Wallet", 10000m);

        var handler = new RecordSplitExpenseHandler(_eventStore);
        await handler.HandleAsync(new RecordSplitExpenseCommand(
            accountId, new DateOnly(2025, 3, 1), 500m, "EGP",
            new List<SplitLineDto> { new("A", 300m, null), new("B", 200m, null) },
            null), _actorUserId, _deviceId, CancellationToken.None);

        var allBefore = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var splitEnv = allBefore.First(e => e.EventType == nameof(SplitExpenseRecorded));
        var splitEv = System.Text.Json.JsonSerializer.Deserialize<SplitExpenseRecorded>(
            splitEnv.PayloadJson, DomainJson.Options)!;

        var revHandler = new ReverseSplitExpenseHandler(_eventStore);
        await revHandler.HandleAsync(new ReverseSplitExpenseCommand(
            splitEv.ParentId, "Test reversal", new DateOnly(2025, 3, 2)),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);

        // Verify the reversal event is in the store
        var reversalEnvs = all.Where(e => e.EventType == nameof(SplitExpenseReversed)).ToList();
        Assert.Single(reversalEnvs);

        // Verify it's deserializable via EnvelopeDeserializer
        var domainEvents = DebtManager.Domain.Services.Serialization.EnvelopeDeserializer.ToDomainEvents(all).ToList();
        var reversalEvents = domainEvents.OfType<SplitExpenseReversed>().ToList();
        Assert.Single(reversalEvents);
        Assert.Equal(splitEv.ParentId, reversalEvents[0].ParentId);
        Assert.Equal("Test reversal", reversalEvents[0].Reason);
    }

    // ================================================================
    // 21) SplitEvents_HaveParentId_AndCorrelationId
    // ================================================================
    [Fact]
    public async Task SplitEvents_HaveParentId_AndCorrelationId()
    {
        var accountId = await CreateAccountAsync("Wallet", 10000m);

        var handler = new RecordSplitExpenseHandler(_eventStore);
        await handler.HandleAsync(new RecordSplitExpenseCommand(
            accountId, new DateOnly(2025, 3, 1), 500m, "EGP",
            new List<SplitLineDto> { new("A", 300m, null), new("B", 200m, null) },
            null), _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var splitEnv = all.First(e => e.EventType == nameof(SplitExpenseRecorded));
        var splitEv = System.Text.Json.JsonSerializer.Deserialize<SplitExpenseRecorded>(
            splitEnv.PayloadJson, DomainJson.Options)!;

        Assert.NotEqual(Guid.Empty, splitEv.ParentId);
        Assert.NotEqual(Guid.Empty, splitEv.CorrelationId);

        // Ledger rows should reference the ParentId, not envelope EventId
        var ledger = CashLedgerProjector.Project(all);
        var splitRows = ledger.Rows.Where(r => r.Reference.StartsWith("SplitExpense:")).ToList();
        Assert.All(splitRows, r => Assert.Contains(splitEv.ParentId.ToString(), r.Reference));
    }

    // ================================================================
    // 22) Import_Split_SharesCorrelationId_FromEvent
    // ================================================================
    [Fact]
    public async Task Import_Split_SharesCorrelationId_FromEvent()
    {
        var accountId = await CreateAccountAsync("Import", 5000m);
        var handler = new RecordSplitExpenseHandler(_eventStore);

        await handler.HandleAsync(new RecordSplitExpenseCommand(
            accountId, new DateOnly(2025, 4, 1), 600m, "EGP",
            new List<SplitLineDto> { new("Rent", 400m, null), new("Utils", 200m, null) },
            "Imported row"),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledger = CashLedgerProjector.Project(all);

        var splitRows = ledger.Rows.Where(r => r.Reference.StartsWith("SplitExpense:")).ToList();
        Assert.Equal(2, splitRows.Count);

        // All share the same CorrelationId (from the event payload)
        var correlationIds = splitRows.Select(r => r.CorrelationId).Distinct().ToList();
        Assert.Single(correlationIds);
        Assert.NotEqual(Guid.Empty, correlationIds[0]);
    }

    // --- Helpers ---

    private async Task AppendEvent<T>(T domainEvent) where T : IDomainEvent
    {
        var streamId = domainEvent switch
        {
            CategoryCreated c => c.CategoryId,
            _ => Guid.NewGuid()
        };

        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(streamId),
            domainEvent.GetType().Name,
            DateTimeOffset.UtcNow,
            domainEvent.EffectiveDate,
            _actorUserId,
            _deviceId,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(domainEvent, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);
    }

    private async Task AppendBudget(int year, int month, string currency, string scopeType,
        Guid? categoryId, Guid? accountId, decimal limit)
    {
        var budgetId = Guid.NewGuid();
        var ev = new BudgetDefined(budgetId, year, month, currency, scopeType,
            categoryId, accountId, limit, "None", new DateOnly(year, month, 1));
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(budgetId),
            nameof(BudgetDefined),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            _actorUserId,
            _deviceId,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);
    }

    private async Task AppendExpenseEvent(Guid accountId, decimal amount, string currency, string category, string notes, DateOnly date)
    {
        var currencyObj = currency switch
        {
            "EGP" => Currency.EGP,
            "USD" => Currency.USD,
            _ => new Currency(currency, 2)
        };

        var ev = new ExpenseRecorded(accountId, new Money(amount, currencyObj), date, category, notes);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(accountId),
            nameof(ExpenseRecorded),
            DateTimeOffset.UtcNow,
            date,
            _actorUserId,
            _deviceId,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);
    }
}
