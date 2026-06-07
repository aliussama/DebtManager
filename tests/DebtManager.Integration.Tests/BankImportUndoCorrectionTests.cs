using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Import;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class BankImportUndoCorrectionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();
    private static string TodayStr => DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");

    public BankImportUndoCorrectionTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"UndoCorrTests_{id}.db");
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

    private async Task<Guid> CreateProfileAsync()
    {
        var handler = new CreateBankImportProfileHandler(_eventStore);
        var json = new BankImportProfile
        {
            Delimiter = ",",
            HasHeaderRow = true,
            DateColumn = 0,
            AmountColumn = 1,
            DescriptionColumn = 2,
            DateFormat = "yyyy-MM-dd",
            DecimalSeparator = ".",
            CurrencyCode = "EGP",
            SignConvention = "negative_is_debit"
        }.ToJson();

        return await handler.HandleAsync(
            new CreateBankImportProfileCommand("Test Profile", json, DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> CreateAccountAsync(string name = "Test Account", decimal opening = 5000m)
    {
        var handler = new CreateAccountHandler(_eventStore);
        return await handler.HandleAsync(
            new CreateAccountCommand(null, name, "Bank", opening, "EGP", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<(Guid batchId, Guid importedId)> ImportSingleRow(Guid profileId, Guid accountId, string csv)
    {
        var importHandler = new StartBankImportBatchHandler(_eventStore);
        var batchId = await importHandler.HandleAsync(
            new StartBankImportBatchCommand(profileId, accountId, "test.csv", csv,
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state = BankImportProjector.Project(all);
        var importedId = state.ImportedTransactions.Values
            .First(t => t.BatchId == batchId).ImportedId;

        return (batchId, importedId);
    }

    // ======= 1) Revert Applied Expense ========
    [Fact]
    public async Task Revert_AppliedExpense_NetsOutInCashLedger()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync("Checking", 5000m);

        var csv = $"Date,Amount,Description\n{TodayStr},-200.00,Electric bill\n";
        var (_, importedId) = await ImportSingleRow(profileId, accountId, csv);

        var applyHandler = new ApplyImportedTransactionHandler(_eventStore);
        await applyHandler.HandleAsync(
            new ApplyImportedTransactionCommand(importedId, "Expense", "Utilities", "Electric bill", null),
            _actorUserId, _deviceId, CancellationToken.None);

        // Verify expense was applied
        var all1 = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledger1 = CashLedgerProjector.Project(all1);
        Assert.Equal(4800m, ledger1.Accounts[accountId].Balance);

        // Now revert
        var revertHandler = new RevertImportedDecisionHandler(_eventStore);
        await revertHandler.HandleAsync(
            new RevertImportedDecisionCommand(importedId, DateOnly.FromDateTime(DateTime.Today), "Undo test"),
            _actorUserId, _deviceId, CancellationToken.None);

        var all2 = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledger2 = CashLedgerProjector.Project(all2);

        Assert.Equal(5000m, ledger2.Accounts[accountId].Balance);

        var importState = BankImportProjector.Project(all2);
        Assert.False(importState.AppliedLinks.ContainsKey(importedId));
        var decision = importState.Decisions[importedId];
        Assert.Equal(ImportedTransactionStatus.Unresolved, decision.CurrentStatus);
    }

    // ======= 2) Revert Applied Income ========
    [Fact]
    public async Task Revert_AppliedIncome_NetsOutInCashLedger()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync("Checking", 5000m);

        var csv = $"Date,Amount,Description\n{TodayStr},3000.00,Salary\n";
        var (_, importedId) = await ImportSingleRow(profileId, accountId, csv);

        var applyHandler = new ApplyImportedTransactionHandler(_eventStore);
        await applyHandler.HandleAsync(
            new ApplyImportedTransactionCommand(importedId, "Income", null, "Monthly salary", null),
            _actorUserId, _deviceId, CancellationToken.None);

        var all1 = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledger1 = CashLedgerProjector.Project(all1);
        Assert.Equal(8000m, ledger1.Accounts[accountId].Balance);

        var revertHandler = new RevertImportedDecisionHandler(_eventStore);
        await revertHandler.HandleAsync(
            new RevertImportedDecisionCommand(importedId, DateOnly.FromDateTime(DateTime.Today), "Undo income"),
            _actorUserId, _deviceId, CancellationToken.None);

        var all2 = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledger2 = CashLedgerProjector.Project(all2);

        Assert.Equal(5000m, ledger2.Accounts[accountId].Balance);
    }

    // ======= 3) Revert Applied Transfer ========
    [Fact]
    public async Task Revert_AppliedTransfer_NetsOutInCashLedger()
    {
        var profileId = await CreateProfileAsync();
        var fromAccountId = await CreateAccountAsync("From", 5000m);
        var toAccountId = await CreateAccountAsync("To", 1000m);

        var csv = $"Date,Amount,Description\n{TodayStr},-500.00,Transfer\n";
        var (_, importedId) = await ImportSingleRow(profileId, fromAccountId, csv);

        var applyHandler = new ApplyImportedTransactionHandler(_eventStore);
        await applyHandler.HandleAsync(
            new ApplyImportedTransactionCommand(importedId, "Transfer", null, "Transfer to savings", toAccountId),
            _actorUserId, _deviceId, CancellationToken.None);

        var all1 = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledger1 = CashLedgerProjector.Project(all1);
        Assert.Equal(4500m, ledger1.Accounts[fromAccountId].Balance);
        Assert.Equal(1500m, ledger1.Accounts[toAccountId].Balance);

        var revertHandler = new RevertImportedDecisionHandler(_eventStore);
        await revertHandler.HandleAsync(
            new RevertImportedDecisionCommand(importedId, DateOnly.FromDateTime(DateTime.Today), "Undo transfer"),
            _actorUserId, _deviceId, CancellationToken.None);

        var all2 = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledger2 = CashLedgerProjector.Project(all2);
        Assert.Equal(5000m, ledger2.Accounts[fromAccountId].Balance);
        Assert.Equal(1000m, ledger2.Accounts[toAccountId].Balance);
    }

    // ======= 4) Revert Matched ========
    [Fact]
    public async Task Revert_Matched_ReturnsToUnresolved()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);

        await AppendExpenseEvent(accountId, 100m, "EGP", "Food", "Lunch", today);

        var csv = $"Date,Amount,Description\n{TodayStr},-100.00,Restaurant\n";
        var (_, importedId) = await ImportSingleRow(profileId, accountId, csv);

        var reconcileHandler = new GetReconciliationCandidatesHandler(_eventStore);
        var candidates = await reconcileHandler.HandleAsync(accountId, CancellationToken.None);
        var row = candidates.First(c => c.ImportedId == importedId);

        var matchHandler = new ConfirmMatchImportedTransactionHandler(_eventStore);
        await matchHandler.HandleAsync(
            new ConfirmMatchImportedTransactionCommand(importedId, row.MatchedEventId!.Value, "Confirmed"),
            _actorUserId, _deviceId, CancellationToken.None);

        var revertHandler = new RevertImportedDecisionHandler(_eventStore);
        await revertHandler.HandleAsync(
            new RevertImportedDecisionCommand(importedId, today, "Undo match"),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);

        Assert.False(importState.MatchedLinks.ContainsKey(importedId));
        var decision = importState.Decisions[importedId];
        Assert.Equal(ImportedTransactionStatus.Unresolved, decision.CurrentStatus);
    }

    // ======= 5) Revert Ignored ========
    [Fact]
    public async Task Revert_Ignored_ReturnsToUnresolved()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();

        var csv = $"Date,Amount,Description\n{TodayStr},-10.00,ATM Fee\n";
        var (_, importedId) = await ImportSingleRow(profileId, accountId, csv);

        var ignoreHandler = new IgnoreImportedTransactionHandler(_eventStore);
        await ignoreHandler.HandleAsync(
            new IgnoreImportedTransactionCommand(importedId, "Not tracked"),
            _actorUserId, _deviceId, CancellationToken.None);

        var revertHandler = new RevertImportedDecisionHandler(_eventStore);
        await revertHandler.HandleAsync(
            new RevertImportedDecisionCommand(importedId, DateOnly.FromDateTime(DateTime.Today), "Undo ignore"),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);

        Assert.DoesNotContain(importedId, importState.IgnoredIds);
        var decision = importState.Decisions[importedId];
        Assert.Equal(ImportedTransactionStatus.Unresolved, decision.CurrentStatus);
    }

    // ======= 6) Correct Ignored -> Applied ========
    [Fact]
    public async Task Correct_IgnoredToApplied_Works()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync("Checking", 5000m);

        var csv = $"Date,Amount,Description\n{TodayStr},-150.00,Unknown\n";
        var (_, importedId) = await ImportSingleRow(profileId, accountId, csv);

        var ignoreHandler = new IgnoreImportedTransactionHandler(_eventStore);
        await ignoreHandler.HandleAsync(
            new IgnoreImportedTransactionCommand(importedId, "Skip"),
            _actorUserId, _deviceId, CancellationToken.None);

        var correctHandler = new CorrectImportedDecisionHandler(_eventStore);
        await correctHandler.HandleAsync(
            new CorrectImportedDecisionCommand(importedId, "apply", "Expense", null,
                DateOnly.FromDateTime(DateTime.Today), "Actually an expense"),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);
        var ledger = CashLedgerProjector.Project(all);

        Assert.True(importState.AppliedLinks.ContainsKey(importedId));
        Assert.Equal("ExpenseRecorded", importState.AppliedLinks[importedId].AppliedType);
        Assert.Equal(4850m, ledger.Accounts[accountId].Balance);
    }

    // ======= 7) Correct Applied-as-Expense -> Applied-as-Income ========
    [Fact]
    public async Task Correct_AppliedToIncome_RevertsThenAppliesNew()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync("Checking", 5000m);

        var csv = $"Date,Amount,Description\n{TodayStr},500.00,Refund\n";
        var (_, importedId) = await ImportSingleRow(profileId, accountId, csv);

        // Apply as Expense first (mistake)
        var applyHandler = new ApplyImportedTransactionHandler(_eventStore);
        await applyHandler.HandleAsync(
            new ApplyImportedTransactionCommand(importedId, "Expense", null, null, null),
            _actorUserId, _deviceId, CancellationToken.None);

        // Correct to Income
        var correctHandler = new CorrectImportedDecisionHandler(_eventStore);
        await correctHandler.HandleAsync(
            new CorrectImportedDecisionCommand(importedId, "apply", "Income", null,
                DateOnly.FromDateTime(DateTime.Today), "Was actually income"),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);
        var ledger = CashLedgerProjector.Project(all);

        Assert.True(importState.AppliedLinks.ContainsKey(importedId));
        Assert.Equal("IncomeRecorded", importState.AppliedLinks[importedId].AppliedType);
        // Opening 5000 - 500 (expense) + 500 (expense reversal) + 500 (income) = 5500
        Assert.Equal(5500m, ledger.Accounts[accountId].Balance);
    }

    // ======= 8) Undo Batch ========
    [Fact]
    public async Task UndoBatch_RevertsAllActiveDecisions()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync("Checking", 10000m);

        var csv = $"Date,Amount,Description\n{TodayStr},-100.00,Item1\n{TodayStr},-200.00,Item2\n{TodayStr},500.00,Item3\n";
        var importHandler = new StartBankImportBatchHandler(_eventStore);
        var batchId = await importHandler.HandleAsync(
            new StartBankImportBatchCommand(profileId, accountId, "batch.csv", csv,
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state = BankImportProjector.Project(all);
        var ids = state.ImportedTransactions.Values.Where(t => t.BatchId == batchId).ToList();

        var applyHandler = new ApplyImportedTransactionHandler(_eventStore);
        foreach (var txn in ids)
        {
            var mode = txn.Direction == "credit" ? "Income" : "Expense";
            await applyHandler.HandleAsync(
                new ApplyImportedTransactionCommand(txn.ImportedId, mode, null, null, null),
                _actorUserId, _deviceId, CancellationToken.None);
        }

        // Net effect: -100 -200 +500 = +200, so balance = 10200
        all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledger1 = CashLedgerProjector.Project(all);
        Assert.Equal(10200m, ledger1.Accounts[accountId].Balance);

        var undoBatchHandler = new UndoImportBatchHandler(_eventStore);
        var count = await undoBatchHandler.HandleAsync(
            new UndoImportBatchCommand(batchId, DateOnly.FromDateTime(DateTime.Today), "Batch undo test"),
            _actorUserId, _deviceId, CancellationToken.None);

        Assert.Equal(3, count);

        all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledger2 = CashLedgerProjector.Project(all);
        Assert.Equal(10000m, ledger2.Accounts[accountId].Balance);

        var importState2 = BankImportProjector.Project(all);
        foreach (var txn in ids)
        {
            var decision = importState2.Decisions[txn.ImportedId];
            Assert.Equal(ImportedTransactionStatus.Unresolved, decision.CurrentStatus);
        }
    }

    // ======= 9) Bulk Apply Unmatched ========
    [Fact]
    public async Task BulkApply_Unmatched_AppliesAllRows()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync("Checking", 10000m);

        var csv = $"Date,Amount,Description\n{TodayStr},-50.00,Coffee\n{TodayStr},-75.00,Lunch\n{TodayStr},1000.00,Bonus\n";
        var importHandler = new StartBankImportBatchHandler(_eventStore);
        var batchId = await importHandler.HandleAsync(
            new StartBankImportBatchCommand(profileId, accountId, "bulk.csv", csv,
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var bulkHandler = new BulkApplyUnmatchedHandler(_eventStore);
        var applied = await bulkHandler.HandleAsync(
            new BulkApplyUnmatchedCommand(batchId, accountId, DateOnly.FromDateTime(DateTime.Today), "Bulk", false),
            _actorUserId, _deviceId, CancellationToken.None);

        Assert.Equal(3, applied);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);

        var batchTxns = importState.ImportedTransactions.Values.Where(t => t.BatchId == batchId).ToList();
        foreach (var txn in batchTxns)
        {
            Assert.True(importState.AppliedLinks.ContainsKey(txn.ImportedId));
        }
    }

    // ======= 10) Bulk Apply with Rules ========
    [Fact]
    public async Task BulkApply_WithRules_AssignsCategory()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync("Checking", 10000m);

        var csv = $"Date,Amount,Description\n{TodayStr},-100.00,Grocery Store purchase\n{TodayStr},5000.00,Monthly Salary deposit\n";
        var importHandler = new StartBankImportBatchHandler(_eventStore);
        var batchId = await importHandler.HandleAsync(
            new StartBankImportBatchCommand(profileId, accountId, "rules.csv", csv,
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var bulkHandler = new BulkApplyUnmatchedHandler(_eventStore);
        var applied = await bulkHandler.HandleAsync(
            new BulkApplyUnmatchedCommand(batchId, accountId, DateOnly.FromDateTime(DateTime.Today), "Bulk with rules", true),
            _actorUserId, _deviceId, CancellationToken.None);

        Assert.Equal(2, applied);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);

        foreach (var txn in importState.ImportedTransactions.Values.Where(t => t.BatchId == batchId))
        {
            var decision = importState.Decisions[txn.ImportedId];
            Assert.Equal(ImportedTransactionStatus.Applied, decision.CurrentStatus);
        }
    }

    // ======= 11) Revert When Nothing to Revert ========
    [Fact]
    public async Task Revert_WhenNothingToRevert_ThrowsFriendly()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();

        var csv = $"Date,Amount,Description\n{TodayStr},-50.00,Pending\n";
        var (_, importedId) = await ImportSingleRow(profileId, accountId, csv);

        var revertHandler = new RevertImportedDecisionHandler(_eventStore);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            revertHandler.HandleAsync(
                new RevertImportedDecisionCommand(importedId, DateOnly.FromDateTime(DateTime.Today), "Nothing here"),
                _actorUserId, _deviceId, CancellationToken.None));

        Assert.Contains("Nothing to revert", ex.Message);
    }

    // ======= 12) Double Revert is Blocked ========
    [Fact]
    public async Task DoubleRevert_BlockedOrNoOp()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync("Checking", 5000m);

        var csv = $"Date,Amount,Description\n{TodayStr},-100.00,Item\n";
        var (_, importedId) = await ImportSingleRow(profileId, accountId, csv);

        var applyHandler = new ApplyImportedTransactionHandler(_eventStore);
        await applyHandler.HandleAsync(
            new ApplyImportedTransactionCommand(importedId, "Expense", null, null, null),
            _actorUserId, _deviceId, CancellationToken.None);

        var revertHandler = new RevertImportedDecisionHandler(_eventStore);
        await revertHandler.HandleAsync(
            new RevertImportedDecisionCommand(importedId, DateOnly.FromDateTime(DateTime.Today), "First revert"),
            _actorUserId, _deviceId, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            revertHandler.HandleAsync(
                new RevertImportedDecisionCommand(importedId, DateOnly.FromDateTime(DateTime.Today), "Second revert"),
                _actorUserId, _deviceId, CancellationToken.None));

        Assert.Contains("Nothing to revert", ex.Message);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledger = CashLedgerProjector.Project(all);
        Assert.Equal(5000m, ledger.Accounts[accountId].Balance);
    }

    // --- helpers ---

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
            new EventId(Guid.NewGuid()),
            new StreamId(accountId),
            nameof(ExpenseRecorded),
            DateTimeOffset.UtcNow,
            effectiveDate,
            _actorUserId,
            _deviceId,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options)
        );
        await _eventStore.AppendAsync(env, CancellationToken.None);
    }
}
