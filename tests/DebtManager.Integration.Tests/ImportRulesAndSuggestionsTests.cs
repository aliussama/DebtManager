using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Import;
using DebtManager.Domain.ImportRules;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class ImportRulesAndSuggestionsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public ImportRulesAndSuggestionsTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"ImportRulesTests_{id}.db");
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

    // ?? Helpers ??????????????????????????????????

    private async Task<Guid> CreateAccountAsync(string name = "Test Account", decimal opening = 5000m)
    {
        var handler = new CreateAccountHandler(_eventStore);
        return await handler.HandleAsync(
            new CreateAccountCommand(null, name, "Bank", opening, "EGP", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> CreateProfileAsync()
    {
        var handler = new CreateBankImportProfileHandler(_eventStore);
        var json = new BankImportProfile
        {
            Delimiter = ",", HasHeaderRow = true, DateColumn = 0, AmountColumn = 1,
            DescriptionColumn = 2, DateFormat = "yyyy-MM-dd", DecimalSeparator = ".",
            CurrencyCode = "EGP", SignConvention = "negative_is_debit"
        }.ToJson();
        return await handler.HandleAsync(
            new CreateBankImportProfileCommand("Test Profile", json, DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> ImportCsvAsync(Guid profileId, Guid accountId, string csv)
    {
        var handler = new StartBankImportBatchHandler(_eventStore);
        return await handler.HandleAsync(
            new StartBankImportBatchCommand(profileId, accountId, "test.csv", csv, DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> CreatePackAsync(string name, bool enabled = true)
    {
        var handler = new CreateImportRulePackHandler(_eventStore);
        return await handler.HandleAsync(
            new CreateImportRulePackCommand(name, "Test pack", enabled, DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> DefineRuleAsync(Guid packId, string kind, string matchJson, string actionJson, int priority = 10)
    {
        var handler = new DefineImportRuleHandler(_eventStore);
        return await handler.HandleAsync(
            new DefineImportRuleCommand(packId, null, 1, kind, matchJson, actionJson, priority, true, DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private string TextContainsCondition(string value) =>
        JsonSerializer.Serialize<ImportCondition>(new TextCondition { Field = "Description", Mode = StringMatchMode.Contains, Value = value }, DomainJson.Options);

    private string AmountEqualsCondition(decimal value, decimal tolerance = 0) =>
        JsonSerializer.Serialize<ImportCondition>(new AmountCondition { Mode = NumberMatchMode.Equals, Value1 = value, ToleranceAbs = tolerance }, DomainJson.Options);

    private string CategorizeActionJson(string category) =>
        JsonSerializer.Serialize<ImportRuleAction>(new CategorizeAction { CategoryName = category }, DomainJson.Options);

    private string IgnoreActionJson(string reason) =>
        JsonSerializer.Serialize<ImportRuleAction>(new IgnoreAction { Reason = reason }, DomainJson.Options);

    private string MatchBillActionJson(decimal tolerance = 1m) =>
        JsonSerializer.Serialize<ImportRuleAction>(new MatchBillAction { MatchMode = "NearestOutstanding", Tolerance = tolerance }, DomainJson.Options);

    private string MatchTransferActionJson() =>
        JsonSerializer.Serialize<ImportRuleAction>(new MatchTransferAction { Direction = "Either", Tolerance = 0 }, DomainJson.Options);

    private GetImportSuggestionsHandler SuggestionsHandler() => new(_eventStore);
    private ApplySuggestionHandler ApplySuggestionHandler() => new(
        _eventStore, new ApplyImportedTransactionHandler(_eventStore),
        new ConfirmMatchImportedTransactionHandler(_eventStore),
        new IgnoreImportedTransactionHandler(_eventStore));
    private RunAutoApplyForBatchHandler AutoApplyHandler() => new(
        _eventStore, SuggestionsHandler(), ApplySuggestionHandler());

    // ?? Tests ????????????????????????????????????

    [Fact]
    public async Task CreatePack_DefineRule_List_Works()
    {
        var packId = await CreatePackAsync("Groceries Pack");
        var ruleId = await DefineRuleAsync(packId, "Categorize", TextContainsCondition("Grocery"), CategorizeActionJson("Groceries"));

        var listHandler = new GetImportRulePacksListHandler(_eventStore);
        var packs = await listHandler.HandleAsync(false, CancellationToken.None);
        Assert.Single(packs);
        Assert.Equal("Groceries Pack", packs[0].Name);

        var detailHandler = new GetImportRulePackDetailHandler(_eventStore);
        var detail = await detailHandler.HandleAsync(packId, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Single(detail!.Rules);
        Assert.Equal("Categorize", detail.Rules[0].Kind);
    }

    [Fact]
    public async Task RuleEngine_TextContains_Categorize_SuggestsWithExplanation()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();
        var batchId = await ImportCsvAsync(profileId, accountId, "Date,Amount,Description\n2025-06-01,-100.50,Grocery Store\n");

        var packId = await CreatePackAsync("TestPack");
        await DefineRuleAsync(packId, "Categorize", TextContainsCondition("Grocery"), CategorizeActionJson("Food"));

        var suggestions = await SuggestionsHandler().HandleAsync(batchId, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);

        Assert.Single(suggestions);
        Assert.Equal("ApplyExpense", suggestions[0].Kind);
        Assert.Equal("Food", suggestions[0].ProposedCategory);
        Assert.True(suggestions[0].Confidence > 0);
        Assert.NotEmpty(suggestions[0].ExplanationLines);
    }

    [Fact]
    public async Task RuleEngine_AmountDateMatch_BillPayment_SuggestsHighConfidence()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();

        // Issue a bill
        var billHandler = new IssueBillHandler(_eventStore);
        var partyHandler = new CreatePartyHandler(_eventStore);
        var partyId = await partyHandler.HandleAsync(
            new CreatePartyCommand(null, "Vendor", "Vendor", "EGP", null, Array.Empty<string>(), DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
        var billId = await billHandler.HandleAsync(
            new IssueBillCommand(null, null, partyId, "EGP", 500m, DateOnly.FromDateTime(DateTime.Today).AddDays(30), "Utilities", "B001", null, DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var batchId = await ImportCsvAsync(profileId, accountId, "Date,Amount,Description\n2025-07-01,-500.00,Utility Payment\n");

        var packId = await CreatePackAsync("BillPack");
        await DefineRuleAsync(packId, "MatchBill", AmountEqualsCondition(500m, 5m), MatchBillActionJson(5m), priority: 20);

        var suggestions = await SuggestionsHandler().HandleAsync(batchId, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);

        Assert.Single(suggestions);
        Assert.Equal("PayBill", suggestions[0].Kind);
        Assert.True(suggestions[0].Confidence >= 80);
        Assert.Equal(billId, suggestions[0].ProposedRelatedEntityId);
    }

    [Fact]
    public async Task RuleEngine_TransferMatch_SuggestsTransfer()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();
        var batchId = await ImportCsvAsync(profileId, accountId, "Date,Amount,Description\n2025-06-15,-1000.00,Transfer to savings\n");

        var packId = await CreatePackAsync("TransferPack");
        await DefineRuleAsync(packId, "MatchTransfer", TextContainsCondition("Transfer"), MatchTransferActionJson());

        var suggestions = await SuggestionsHandler().HandleAsync(batchId, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);

        Assert.Single(suggestions);
        Assert.Equal("ApplyTransfer", suggestions[0].Kind);
    }

    [Fact]
    public async Task Suggestions_AreDeterministic_SameEventsSameSuggestions()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();
        var batchId = await ImportCsvAsync(profileId, accountId, "Date,Amount,Description\n2025-06-01,-50.00,Coffee Shop\n");

        var packId = await CreatePackAsync("CoffeePack");
        await DefineRuleAsync(packId, "Categorize", TextContainsCondition("Coffee"), CategorizeActionJson("Dining"));

        var handler = SuggestionsHandler();
        var suggestions1 = await handler.HandleAsync(batchId, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
        var suggestions2 = await handler.HandleAsync(batchId, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);

        Assert.Equal(suggestions1.Count, suggestions2.Count);
        for (int i = 0; i < suggestions1.Count; i++)
        {
            Assert.Equal(suggestions1[i].SuggestionId, suggestions2[i].SuggestionId);
            Assert.Equal(suggestions1[i].Confidence, suggestions2[i].Confidence);
            Assert.Equal(suggestions1[i].Kind, suggestions2[i].Kind);
        }
    }

    [Fact]
    public async Task ApplySuggestion_Expense_WritesExpenseRecorded_AndAppliedEvent()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();
        var batchId = await ImportCsvAsync(profileId, accountId, "Date,Amount,Description\n2025-06-01,-200.00,Electric bill\n");

        var packId = await CreatePackAsync("UtilitiesPack");
        await DefineRuleAsync(packId, "Categorize", TextContainsCondition("Electric"), CategorizeActionJson("Utilities"));

        var suggestions = await SuggestionsHandler().HandleAsync(batchId, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
        Assert.Single(suggestions);

        var s = suggestions[0];
        await ApplySuggestionHandler().HandleAsync(
            new ApplySuggestionCommand(s.ImportedTransactionId, s.SuggestionId, s.Kind, s.ProposedAccountId, s.ProposedCategory, s.ProposedRelatedEntityId, s.Notes, false),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);
        Assert.True(importState.AppliedLinks.ContainsKey(s.ImportedTransactionId));
        Assert.Equal("ExpenseRecorded", importState.AppliedLinks[s.ImportedTransactionId].AppliedType);
    }

    [Fact]
    public async Task ApplySuggestion_BillPayment_WritesBillPaymentEvents_AndCashExpense()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();

        var partyHandler = new CreatePartyHandler(_eventStore);
        var partyId = await partyHandler.HandleAsync(
            new CreatePartyCommand(null, "Vendor", "Vendor", "EGP", null, Array.Empty<string>(), DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
        var billHandler = new IssueBillHandler(_eventStore);
        await billHandler.HandleAsync(
            new IssueBillCommand(null, null, partyId, "EGP", 300m, DateOnly.FromDateTime(DateTime.Today).AddDays(30), "Rent", "R001", null, DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var batchId = await ImportCsvAsync(profileId, accountId, "Date,Amount,Description\n2025-06-15,-300.00,Rent Payment\n");

        var packId = await CreatePackAsync("RentPack");
        await DefineRuleAsync(packId, "MatchBill", AmountEqualsCondition(300m, 1m), MatchBillActionJson(1m));

        var suggestions = await SuggestionsHandler().HandleAsync(batchId, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
        Assert.Single(suggestions);
        Assert.Equal("PayBill", suggestions[0].Kind);

        var s = suggestions[0];
        await ApplySuggestionHandler().HandleAsync(
            new ApplySuggestionCommand(s.ImportedTransactionId, s.SuggestionId, s.Kind, s.ProposedAccountId, s.ProposedCategory, s.ProposedRelatedEntityId, s.Notes, false),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);
        Assert.True(importState.AppliedLinks.ContainsKey(s.ImportedTransactionId));
    }

    [Fact]
    public async Task ApplySuggestion_MatchOnly_WritesMatchedEvent()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();
        var batchId = await ImportCsvAsync(profileId, accountId, "Date,Amount,Description\n2025-06-01,-75.00,Restaurant\n");

        // Write an existing expense to match against
        var expenseEv = new ExpenseRecorded(accountId, new Money(75m, Currency.EGP), new DateOnly(2025, 6, 1), "Food", "Restaurant");
        var expenseEvId = Guid.NewGuid();
        await _eventStore.AppendAsync(new EventEnvelope(
            new EventId(expenseEvId), new StreamId(accountId),
            nameof(ExpenseRecorded), DateTimeOffset.UtcNow, new DateOnly(2025, 6, 1),
            _actorUserId, _deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(expenseEv, DomainJson.Options)), CancellationToken.None);

        // Manually match
        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);
        var importedId = importState.ImportedTransactions.Values.First().ImportedId;

        await ApplySuggestionHandler().HandleAsync(
            new ApplySuggestionCommand(importedId, "manual", "MatchOnly", null, null, expenseEvId, "Manual match", false),
            _actorUserId, _deviceId, CancellationToken.None);

        all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        importState = BankImportProjector.Project(all);
        Assert.True(importState.MatchedLinks.ContainsKey(importedId));
    }

    [Fact]
    public async Task ApplySuggestion_Ignore_WritesIgnoredEvent()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();
        var batchId = await ImportCsvAsync(profileId, accountId, "Date,Amount,Description\n2025-06-01,-5.00,ATM Fee\n");

        var packId = await CreatePackAsync("IgnorePack");
        await DefineRuleAsync(packId, "Ignore", TextContainsCondition("ATM Fee"), IgnoreActionJson("Bank fee - not tracked"));

        var suggestions = await SuggestionsHandler().HandleAsync(batchId, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
        Assert.Single(suggestions);
        Assert.Equal("Ignore", suggestions[0].Kind);

        var s = suggestions[0];
        await ApplySuggestionHandler().HandleAsync(
            new ApplySuggestionCommand(s.ImportedTransactionId, s.SuggestionId, s.Kind, null, null, null, s.Notes, false),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var importState = BankImportProjector.Project(all);
        Assert.Contains(s.ImportedTransactionId, importState.IgnoredIds);
    }

    [Fact]
    public async Task AutoApply_RespectsThreshold_OnlyAppliesAbove()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();
        var batchId = await ImportCsvAsync(profileId, accountId,
            "Date,Amount,Description\n2025-06-01,-100.00,Grocery Store\n2025-06-02,-50.00,Something Unknown\n");

        var packId = await CreatePackAsync("GroceryPack");
        // Only "Grocery" matches — priority 10 gives confidence 80
        await DefineRuleAsync(packId, "Categorize", TextContainsCondition("Grocery"), CategorizeActionJson("Food"), priority: 10);

        var count = await AutoApplyHandler().HandleAsync(
            new RunAutoApplyCommand(batchId, true, 85, DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Confidence=80 < threshold=85, so nothing applied
        Assert.Equal(0, count);

        // Lower threshold to 75
        var count2 = await AutoApplyHandler().HandleAsync(
            new RunAutoApplyCommand(batchId, true, 75, DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        Assert.Equal(1, count2);
    }

    [Fact]
    public async Task AutoApply_IsIdempotent_DoesNotDoubleApply()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();
        var batchId = await ImportCsvAsync(profileId, accountId, "Date,Amount,Description\n2025-06-01,-100.00,Grocery Store\n");

        var packId = await CreatePackAsync("Pack1");
        await DefineRuleAsync(packId, "Categorize", TextContainsCondition("Grocery"), CategorizeActionJson("Food"), priority: 10);

        var count1 = await AutoApplyHandler().HandleAsync(
            new RunAutoApplyCommand(batchId, true, 70, DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
        Assert.Equal(1, count1);

        // Second run: should not apply again
        var count2 = await AutoApplyHandler().HandleAsync(
            new RunAutoApplyCommand(batchId, true, 70, DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
        Assert.Equal(0, count2);
    }

    [Fact]
    public async Task ArchivedRule_NotUsedInSuggestions()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();
        var batchId = await ImportCsvAsync(profileId, accountId, "Date,Amount,Description\n2025-06-01,-100.00,Grocery Store\n");

        var packId = await CreatePackAsync("Pack1");
        var ruleId = await DefineRuleAsync(packId, "Categorize", TextContainsCondition("Grocery"), CategorizeActionJson("Food"));

        // Archive the rule
        var archiveHandler = new ArchiveImportRuleHandler(_eventStore);
        await archiveHandler.HandleAsync(
            new ArchiveImportRuleCommand(packId, ruleId, "No longer needed", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var suggestions = await SuggestionsHandler().HandleAsync(batchId, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task PackDisabled_NotUsedInSuggestions()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();
        var batchId = await ImportCsvAsync(profileId, accountId, "Date,Amount,Description\n2025-06-01,-100.00,Grocery Store\n");

        // Create disabled pack
        var packId = await CreatePackAsync("DisabledPack", enabled: false);
        await DefineRuleAsync(packId, "Categorize", TextContainsCondition("Grocery"), CategorizeActionJson("Food"));

        var suggestions = await SuggestionsHandler().HandleAsync(batchId, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task PreviewRuleAgainstBatch_DoesNotWriteEvents()
    {
        var profileId = await CreateProfileAsync();
        var accountId = await CreateAccountAsync();
        var batchId = await ImportCsvAsync(profileId, accountId, "Date,Amount,Description\n2025-06-01,-100.00,Grocery Store\n");

        var packId = await CreatePackAsync("PreviewPack");
        await DefineRuleAsync(packId, "Categorize", TextContainsCondition("Grocery"), CategorizeActionJson("Food"));

        var allBefore = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var countBefore = allBefore.Count;

        var previewHandler = new PreviewRuleAgainstBatchHandler(_eventStore);
        var results = await previewHandler.HandleAsync(batchId, CancellationToken.None);

        var allAfter = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        Assert.Equal(countBefore, allAfter.Count);
        Assert.NotEmpty(results);
    }
}
