using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Import;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class BankImportAndReconciliationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public BankImportAndReconciliationTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"BankImportTests_{id}.db");
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

    private async Task<Guid> CreateProfileAsync(string? mappingJson = null)
    {
        var handler = new CreateBankImportProfileHandler(_eventStore);
        var json = mappingJson ?? new BankImportProfile
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

    private const string SampleCsv = "Date,Amount,Description\n2025-06-01,-100.50,Grocery Store\n2025-06-02,2500.00,Salary Deposit\n2025-06-03,-45.00,Gas Station\n";

    [Fact]
    public async Task Preview_DoesNotWriteEvents()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();

        var previewHandler = new PreviewBankImportHandler(_eventStore);
        var allBefore = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var countBefore = allBefore.Count;

        var result = await previewHandler.HandleAsync(
            new PreviewBankImportCommand(profileId, accountId, SampleCsv), CancellationToken.None);

        var allAfter = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);

        Assert.Equal(countBefore, allAfter.Count);
        Assert.Equal(3, result.Rows.Count);
        Assert.False(result.IsDuplicateBatch);
    }

    [Fact]
    public async Task Import_WritesBatchAndTransactions()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();

        var importHandler = new StartBankImportBatchHandler(_eventStore);
        var batchId = await importHandler.HandleAsync(
            new StartBankImportBatchCommand(profileId, accountId, "test.csv", SampleCsv,
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, batchId);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);

        Assert.Single(importState.Batches);
        Assert.True(importState.Batches[batchId].IsCompleted);
        Assert.Equal(3, importState.ImportedTransactions.Count);
    }

    [Fact]
    public async Task Import_SameFileHash_IsIdempotent()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();

        var importHandler = new StartBankImportBatchHandler(_eventStore);

        var batchId1 = await importHandler.HandleAsync(
            new StartBankImportBatchCommand(profileId, accountId, "test.csv", SampleCsv,
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var batchId2 = await importHandler.HandleAsync(
            new StartBankImportBatchCommand(profileId, accountId, "test.csv", SampleCsv,
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        Assert.Equal(batchId1, batchId2);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);
        Assert.Single(importState.Batches);
        Assert.Equal(3, importState.ImportedTransactions.Count);
    }

    [Fact]
    public async Task Import_DedupesDuplicateRowsWithinSameFile()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();

        var csvWithDupes = "Date,Amount,Description\n2025-06-01,-100.50,Grocery Store\n2025-06-01,-100.50,Grocery Store\n2025-06-02,2500.00,Salary\n";

        var importHandler = new StartBankImportBatchHandler(_eventStore);
        await importHandler.HandleAsync(
            new StartBankImportBatchCommand(profileId, accountId, "dupes.csv", csvWithDupes,
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);

        Assert.Equal(2, importState.ImportedTransactions.Count);
        var batch = importState.Batches.Values.Single();
        Assert.Equal(2, batch.ImportedCount);
        Assert.Equal(1, batch.SkippedDuplicatesCount);
    }

    [Fact]
    public async Task Reconciliation_SuggestsExactMatch_WhenSameDateAmount()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();

        // Record an expense in ledger on 2025-06-01 for 100.50
        await AppendExpenseEvent(accountId, 100.50m, "EGP", "Groceries", "Store purchase", new DateOnly(2025, 6, 1));

        // Import CSV with matching row
        var csv = "Date,Amount,Description\n2025-06-01,-100.50,Grocery Store\n";
        var importHandler = new StartBankImportBatchHandler(_eventStore);
        await importHandler.HandleAsync(
            new StartBankImportBatchCommand(profileId, accountId, "match.csv", csv,
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var reconcileHandler = new GetReconciliationCandidatesHandler(_eventStore);
        var candidates = await reconcileHandler.HandleAsync(accountId, CancellationToken.None);

        Assert.Single(candidates);
        Assert.Equal("SuggestedMatch", candidates[0].Status);
        Assert.Equal("Exact", candidates[0].MatchType);
        Assert.True(candidates[0].Confidence >= 0.8m);
    }

    [Fact]
    public async Task ApplyImported_AsExpense_CreatesExpenseRecorded_AndAppliedEvent()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();

        var csv = "Date,Amount,Description\n2025-06-01,-200.00,Electric bill\n";
        var importHandler = new StartBankImportBatchHandler(_eventStore);
        await importHandler.HandleAsync(
            new StartBankImportBatchCommand(profileId, accountId, "bill.csv", csv,
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);
        var importedId = importState.ImportedTransactions.Keys.Single();

        var applyHandler = new ApplyImportedTransactionHandler(_eventStore);
        await applyHandler.HandleAsync(
            new ApplyImportedTransactionCommand(importedId, "Expense", "Utilities", "Electric bill", null),
            _actorUserId, _deviceId, CancellationToken.None);

        // Verify applied link exists
        all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        importState = BankImportProjector.Project(all);
        Assert.True(importState.AppliedLinks.ContainsKey(importedId));
        Assert.Equal("ExpenseRecorded", importState.AppliedLinks[importedId].AppliedType);

        // Verify expense appears in ledger
        var ledgerState = CashLedgerProjector.Project(all);
        var expenseRow = ledgerState.Rows.FirstOrDefault(r => r.Category == "Utilities" && r.Amount == 200m);
        Assert.NotNull(expenseRow);
    }

    [Fact]
    public async Task ApplyImported_Twice_IsBlocked()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();

        var csv = "Date,Amount,Description\n2025-06-01,-50.00,Coffee\n";
        var importHandler = new StartBankImportBatchHandler(_eventStore);
        await importHandler.HandleAsync(
            new StartBankImportBatchCommand(profileId, accountId, "coffee.csv", csv,
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);
        var importedId = importState.ImportedTransactions.Keys.Single();

        var applyHandler = new ApplyImportedTransactionHandler(_eventStore);
        await applyHandler.HandleAsync(
            new ApplyImportedTransactionCommand(importedId, "Expense", null, null, null),
            _actorUserId, _deviceId, CancellationToken.None);

        // Second apply should be blocked
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            applyHandler.HandleAsync(
                new ApplyImportedTransactionCommand(importedId, "Expense", null, null, null),
                _actorUserId, _deviceId, CancellationToken.None));
    }

    [Fact]
    public async Task MatchImported_WritesMatchedEvent()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();

        await AppendExpenseEvent(accountId, 75m, "EGP", "Food", "Restaurant", new DateOnly(2025, 6, 5));

        var csv = "Date,Amount,Description\n2025-06-05,-75.00,Restaurant lunch\n";
        var importHandler = new StartBankImportBatchHandler(_eventStore);
        await importHandler.HandleAsync(
            new StartBankImportBatchCommand(profileId, accountId, "restaurant.csv", csv,
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var reconcileHandler = new GetReconciliationCandidatesHandler(_eventStore);
        var candidates = await reconcileHandler.HandleAsync(accountId, CancellationToken.None);

        Assert.Single(candidates);
        Assert.NotNull(candidates[0].MatchedEventId);

        var matchHandler = new ConfirmMatchImportedTransactionHandler(_eventStore);
        await matchHandler.HandleAsync(
            new ConfirmMatchImportedTransactionCommand(candidates[0].ImportedId, candidates[0].MatchedEventId!.Value, "Confirmed match"),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);
        Assert.True(importState.MatchedLinks.ContainsKey(candidates[0].ImportedId));
        Assert.Equal("Manual", importState.MatchedLinks[candidates[0].ImportedId].MatchType);
    }

    [Fact]
    public async Task IgnoreImported_WritesIgnoredEvent()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();

        var csv = "Date,Amount,Description\n2025-06-01,-10.00,ATM Fee\n";
        var importHandler = new StartBankImportBatchHandler(_eventStore);
        await importHandler.HandleAsync(
            new StartBankImportBatchCommand(profileId, accountId, "fee.csv", csv,
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);
        var importedId = importState.ImportedTransactions.Keys.Single();

        var ignoreHandler = new IgnoreImportedTransactionHandler(_eventStore);
        await ignoreHandler.HandleAsync(
            new IgnoreImportedTransactionCommand(importedId, "ATM fee - not tracked"),
            _actorUserId, _deviceId, CancellationToken.None);

        all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        importState = BankImportProjector.Project(all);
        Assert.Contains(importedId, importState.IgnoredIds);
    }

    [Fact]
    public async Task AppliedTransactions_AppearInCashLedger()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync("Checking", 10000m);

        var csv = "Date,Amount,Description\n2025-06-10,3000.00,Monthly Salary\n2025-06-11,-500.00,Rent Payment\n";
        var importHandler = new StartBankImportBatchHandler(_eventStore);
        await importHandler.HandleAsync(
            new StartBankImportBatchCommand(profileId, accountId, "salary_rent.csv", csv,
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);

        var applyHandler = new ApplyImportedTransactionHandler(_eventStore);

        foreach (var txn in importState.ImportedTransactions.Values)
        {
            var mode = txn.Direction == "credit" ? "Income" : "Expense";
            await applyHandler.HandleAsync(
                new ApplyImportedTransactionCommand(txn.ImportedId, mode, null, null, null),
                _actorUserId, _deviceId, CancellationToken.None);
        }

        var ledgerHandler = new GetCashLedgerHandler(_eventStore);
        var ledger = await ledgerHandler.HandleAsync(new CashLedgerQuery(AccountId: accountId), CancellationToken.None);

        // Opening balance + salary income + rent expense
        Assert.Contains(ledger.Rows, r => r.Direction == "In" && r.Amount == 3000m);
        Assert.Contains(ledger.Rows, r => r.Direction == "Out" && r.Amount == 500m);
    }

    [Fact]
    public async Task Profile_CreateModifyArchive_Works()
    {
        var createHandler = new CreateBankImportProfileHandler(_eventStore);
        var modifyHandler = new ModifyBankImportProfileHandler(_eventStore);
        var archiveHandler = new ArchiveBankImportProfileHandler(_eventStore);
        var getHandler = new GetBankImportProfilesListHandler(_eventStore);

        var profileId = await createHandler.HandleAsync(
            new CreateBankImportProfileCommand("Original", "{}", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var profiles = await getHandler.HandleAsync(CancellationToken.None);
        Assert.Single(profiles);
        Assert.Equal("Original", profiles[0].Name);

        await modifyHandler.HandleAsync(
            new ModifyBankImportProfileCommand(profileId, "{\"updated\": true}",
                DateOnly.FromDateTime(DateTime.Today), "Updated config"),
            _actorUserId, _deviceId, CancellationToken.None);

        profiles = await getHandler.HandleAsync(CancellationToken.None);
        Assert.Contains("{\"updated\": true}", profiles[0].MappingJson);

        await archiveHandler.HandleAsync(
            new ArchiveBankImportProfileCommand(profileId, DateOnly.FromDateTime(DateTime.Today), "No longer needed"),
            _actorUserId, _deviceId, CancellationToken.None);

        profiles = await getHandler.HandleAsync(CancellationToken.None);
        Assert.True(profiles[0].IsArchived);
    }

    [Fact]
    public void Parser_HandlesSemicolonDelimiter_AndDecimalComma()
    {
        var profile = new BankImportProfile
        {
            Delimiter = ";",
            HasHeaderRow = true,
            DateColumn = 0,
            AmountColumn = 1,
            DescriptionColumn = 2,
            DateFormat = "dd.MM.yyyy",
            DecimalSeparator = ",",
            CurrencyCode = "EUR",
            SignConvention = "negative_is_debit"
        };

        var csv = "Datum;Betrag;Beschreibung\n01.06.2025;-1.234,56;Supermarkt\n02.06.2025;2.500,00;Gehalt\n";

        var rows = BankCsvParser.Parse(csv, profile);

        Assert.Equal(2, rows.Count);

        Assert.Equal(new DateOnly(2025, 6, 1), rows[0].TxnDate);
        Assert.Equal(1234.56m, rows[0].Amount);
        Assert.Equal("debit", rows[0].Direction);
        Assert.Equal("Supermarkt", rows[0].Description);
        Assert.Equal("EUR", rows[0].CurrencyCode);

        Assert.Equal(new DateOnly(2025, 6, 2), rows[1].TxnDate);
        Assert.Equal(2500.00m, rows[1].Amount);
        Assert.Equal("credit", rows[1].Direction);
        Assert.Equal("Gehalt", rows[1].Description);
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
