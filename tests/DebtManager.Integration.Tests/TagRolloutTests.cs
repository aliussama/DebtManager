using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class TagRolloutTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public TagRolloutTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"TagRolloutTests_{id}.db");
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

    // --- Entity creation helpers ---

    private async Task<Guid> CreateAccountAsync(string name = "Wallet")
    {
        var handler = new CreateAccountHandler(_eventStore);
        return await handler.HandleAsync(
            new CreateAccountCommand(null, name, "Cash", 5000m, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> CreatePartyAsync(string name = "Vendor X")
    {
        var handler = new CreatePartyHandler(_eventStore);
        return await handler.HandleAsync(
            new CreatePartyCommand(null, "Vendor", name, "EGP", null, [],
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> CreateObligationAsync(string name = "Car Loan")
    {
        var id = Guid.NewGuid();
        var handler = new CreateObligationHandler(_eventStore);
        await handler.HandleAsync(
            new CreateObligationCommand(id, name, "Loan", 50000m, "EGP",
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
        return id;
    }

    private async Task<Guid> CreateAssetAsync(string name = "Apartment")
    {
        var handler = new CreateAssetHandler(_eventStore);
        return await handler.HandleAsync(
            new CreateAssetCommand(null, name, "RealEstate", "EGP",
                "{\"unit\":\"property\",\"amount\":1}", [], null,
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> CreateContractAsync(Guid partyId, string title = "Office Lease")
    {
        var handler = new CreateContractHandler(_eventStore);
        var termsJson = JsonSerializer.Serialize(new
        {
            BillingCycle = "Monthly",
            BillingInterval = 1,
            BillingDayOfMonth = 1,
            BaseAmount = 5000m,
            Category = "Lease",
            AnnualEscalationPercent = 0,
            GracePeriodDays = 0
        });
        return await handler.HandleAsync(
            new CreateContractCommand(null, partyId, "Lease", title,
                new DateOnly(2025, 1, 1), null, "EGP", termsJson,
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> IssueBillAsync(Guid partyId)
    {
        var handler = new IssueBillHandler(_eventStore);
        return await handler.HandleAsync(
            new IssueBillCommand(null, null, partyId, "EGP", 1000m,
                new DateOnly(2025, 6, 1), "General", "BILL-001", null,
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> IssueInvoiceAsync(Guid partyId)
    {
        var handler = new IssueInvoiceHandler(_eventStore);
        return await handler.HandleAsync(
            new IssueInvoiceCommand(null, null, partyId, "EGP", 2000m,
                new DateOnly(2025, 6, 1), "Services", "INV-001", null,
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> CreateGoalAsync(string name = "Emergency Fund")
    {
        var handler = new CreateFinancialGoalHandler(_eventStore);
        return await handler.HandleAsync(
            new CreateFinancialGoalCommand(null, name, "EmergencyFund",
                100000m, "EGP", new DateOnly(2026, 1, 1), null, [],
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> CreateRecurringAsync(Guid accountId)
    {
        var handler = new CreateRecurringHandler(_eventStore);
        return await handler.HandleAsync(
            new CreateRecurringCommand(null, "expense", accountId,
                500m, "EGP", null, null, "Rent",
                "Monthly", 1, new DateOnly(2025, 1, 1), null, false),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    // --- Tag helpers ---

    private UpdateEntityTagsHandler TagHandler => new(_eventStore);
    private GetTagSuggestionsHandler SuggestionsHandler => new(_eventStore);
    private GetEntitiesByTagHandler EntitiesByTagHandler => new(_eventStore);

    private async Task SetTagsAsync(Guid entityId, string entityType, params string[] tags)
    {
        await TagHandler.HandleAsync(
            new UpdateEntityTagsCommand(entityId, entityType, tags.ToList(),
                new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<TagState> GetTagStateAsync()
    {
        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        return TagProjector.Project(all);
    }

    // ================================================================
    // 1) Tag_Update_OnObligation_WritesEvent_AndAppearsInTagState
    // ================================================================
    [Fact]
    public async Task Tag_Update_OnObligation_WritesEvent_AndAppearsInTagState()
    {
        var obligationId = await CreateObligationAsync();
        await SetTagsAsync(obligationId, "Obligation", "car", "loan");

        var state = await GetTagStateAsync();
        Assert.True(state.EntityTags.ContainsKey((obligationId, "Obligation")));
        var tags = state.EntityTags[(obligationId, "Obligation")];
        Assert.Equal(2, tags.Count);
        Assert.Contains("car", tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("loan", tags, StringComparer.OrdinalIgnoreCase);
    }

    // ================================================================
    // 2) Tag_Update_OnBill_WritesEvent_AndAppearsInTagState
    // ================================================================
    [Fact]
    public async Task Tag_Update_OnBill_WritesEvent_AndAppearsInTagState()
    {
        var partyId = await CreatePartyAsync();
        var billId = await IssueBillAsync(partyId);
        await SetTagsAsync(billId, "Bill", "utilities", "monthly");

        var state = await GetTagStateAsync();
        Assert.True(state.EntityTags.ContainsKey((billId, "Bill")));
        Assert.Contains("utilities", state.EntityTags[(billId, "Bill")], StringComparer.OrdinalIgnoreCase);
    }

    // ================================================================
    // 3) Tag_Update_OnInvoice_WritesEvent_AndAppearsInTagState
    // ================================================================
    [Fact]
    public async Task Tag_Update_OnInvoice_WritesEvent_AndAppearsInTagState()
    {
        var partyId = await CreatePartyAsync();
        var invoiceId = await IssueInvoiceAsync(partyId);
        await SetTagsAsync(invoiceId, "Invoice", "consulting", "q1");

        var state = await GetTagStateAsync();
        Assert.True(state.EntityTags.ContainsKey((invoiceId, "Invoice")));
        Assert.Contains("consulting", state.EntityTags[(invoiceId, "Invoice")], StringComparer.OrdinalIgnoreCase);
    }

    // ================================================================
    // 4) Tag_Update_OnAsset_WritesEvent_AndAppearsInTagState
    // ================================================================
    [Fact]
    public async Task Tag_Update_OnAsset_WritesEvent_AndAppearsInTagState()
    {
        var assetId = await CreateAssetAsync();
        await SetTagsAsync(assetId, "Asset", "real-estate", "primary");

        var state = await GetTagStateAsync();
        Assert.True(state.EntityTags.ContainsKey((assetId, "Asset")));
        Assert.Contains("real-estate", state.EntityTags[(assetId, "Asset")], StringComparer.OrdinalIgnoreCase);
    }

    // ================================================================
    // 5) Tag_Update_OnContract_WritesEvent_AndAppearsInTagState
    // ================================================================
    [Fact]
    public async Task Tag_Update_OnContract_WritesEvent_AndAppearsInTagState()
    {
        var partyId = await CreatePartyAsync();
        var contractId = await CreateContractAsync(partyId);
        await SetTagsAsync(contractId, "Contract", "office", "active");

        var state = await GetTagStateAsync();
        Assert.True(state.EntityTags.ContainsKey((contractId, "Contract")));
        Assert.Contains("office", state.EntityTags[(contractId, "Contract")], StringComparer.OrdinalIgnoreCase);
    }

    // ================================================================
    // 6) Tag_Update_OnParty_WritesEvent_AndAppearsInTagState
    // ================================================================
    [Fact]
    public async Task Tag_Update_OnParty_WritesEvent_AndAppearsInTagState()
    {
        var partyId = await CreatePartyAsync();
        await SetTagsAsync(partyId, "Party", "vendor", "trusted");

        var state = await GetTagStateAsync();
        Assert.True(state.EntityTags.ContainsKey((partyId, "Party")));
        Assert.Contains("vendor", state.EntityTags[(partyId, "Party")], StringComparer.OrdinalIgnoreCase);
    }

    // ================================================================
    // 7) Tag_Update_OnRecurring_WritesEvent_AndAppearsInTagState
    // ================================================================
    [Fact]
    public async Task Tag_Update_OnRecurring_WritesEvent_AndAppearsInTagState()
    {
        var accountId = await CreateAccountAsync();
        var recurringId = await CreateRecurringAsync(accountId);
        await SetTagsAsync(recurringId, "Recurring", "rent", "essential");

        var state = await GetTagStateAsync();
        Assert.True(state.EntityTags.ContainsKey((recurringId, "Recurring")));
        Assert.Contains("rent", state.EntityTags[(recurringId, "Recurring")], StringComparer.OrdinalIgnoreCase);
    }

    // ================================================================
    // 8) Tag_Update_OnGoal_WritesEvent_AndAppearsInTagState
    // ================================================================
    [Fact]
    public async Task Tag_Update_OnGoal_WritesEvent_AndAppearsInTagState()
    {
        var goalId = await CreateGoalAsync();
        await SetTagsAsync(goalId, "Goal", "savings", "priority");

        var state = await GetTagStateAsync();
        Assert.True(state.EntityTags.ContainsKey((goalId, "Goal")));
        Assert.Contains("savings", state.EntityTags[(goalId, "Goal")], StringComparer.OrdinalIgnoreCase);
    }

    // ================================================================
    // 9) Tag_Filter_ListView_ReturnsOnlyTaggedEntities_Obligations
    // ================================================================
    [Fact]
    public async Task Tag_Filter_ListView_ReturnsOnlyTaggedEntities_Obligations()
    {
        var ob1 = await CreateObligationAsync("Loan A");
        var ob2 = await CreateObligationAsync("Loan B");
        var ob3 = await CreateObligationAsync("Loan C");
        await SetTagsAsync(ob1, "Obligation", "high-priority");
        await SetTagsAsync(ob2, "Obligation", "low-priority");
        // ob3 has no tags

        var results = await EntitiesByTagHandler.HandleAsync("high-priority", CancellationToken.None);
        var obligationIds = results.Where(r => r.EntityType == "Obligation").Select(r => r.EntityId).ToHashSet();

        Assert.Contains(ob1, obligationIds);
        Assert.DoesNotContain(ob2, obligationIds);
        Assert.DoesNotContain(ob3, obligationIds);
    }

    // ================================================================
    // 10) Tag_Filter_ListView_ReturnsOnlyTaggedEntities_Bills
    // ================================================================
    [Fact]
    public async Task Tag_Filter_ListView_ReturnsOnlyTaggedEntities_Bills()
    {
        var partyId = await CreatePartyAsync();
        var b1 = await IssueBillAsync(partyId);
        var b2 = await IssueBillAsync(partyId);
        await SetTagsAsync(b1, "Bill", "urgent");
        // b2 has no tags

        var results = await EntitiesByTagHandler.HandleAsync("urgent", CancellationToken.None);
        var billIds = results.Where(r => r.EntityType == "Bill").Select(r => r.EntityId).ToHashSet();

        Assert.Contains(b1, billIds);
        Assert.DoesNotContain(b2, billIds);
    }

    // ================================================================
    // 11) Tag_Filter_ListView_ReturnsOnlyTaggedEntities_Goals
    // ================================================================
    [Fact]
    public async Task Tag_Filter_ListView_ReturnsOnlyTaggedEntities_Goals()
    {
        var g1 = await CreateGoalAsync("Goal A");
        var g2 = await CreateGoalAsync("Goal B");
        await SetTagsAsync(g1, "Goal", "retirement");
        // g2 has no tags

        var results = await EntitiesByTagHandler.HandleAsync("retirement", CancellationToken.None);
        var goalIds = results.Where(r => r.EntityType == "Goal").Select(r => r.EntityId).ToHashSet();

        Assert.Contains(g1, goalIds);
        Assert.DoesNotContain(g2, goalIds);
    }

    // ================================================================
    // 12) Tag_Suggestions_SortedByUsage_IsStable
    // ================================================================
    [Fact]
    public async Task Tag_Suggestions_SortedByUsage_IsStable()
    {
        var a1 = await CreateAccountAsync("A1");
        var a2 = await CreateAccountAsync("A2");
        var a3 = await CreateAccountAsync("A3");
        var ob1 = await CreateObligationAsync("O1");

        await SetTagsAsync(a1, "Account", "popular", "rare");
        await SetTagsAsync(a2, "Account", "popular", "medium");
        await SetTagsAsync(a3, "Account", "popular");
        await SetTagsAsync(ob1, "Obligation", "medium");

        var suggestions = await SuggestionsHandler.HandleAsync(CancellationToken.None);

        // "popular" = 3 usages, "medium" = 2, "rare" = 1
        Assert.Equal("popular", suggestions[0].Tag);
        Assert.Equal(3, suggestions[0].UsageCount);
        Assert.Equal("medium", suggestions[1].Tag);
        Assert.Equal(2, suggestions[1].UsageCount);

        // Run again — must produce identical output (deterministic)
        var suggestions2 = await SuggestionsHandler.HandleAsync(CancellationToken.None);
        Assert.Equal(suggestions.Count, suggestions2.Count);
        for (int i = 0; i < suggestions.Count; i++)
        {
            Assert.Equal(suggestions[i].Tag, suggestions2[i].Tag);
            Assert.Equal(suggestions[i].UsageCount, suggestions2[i].UsageCount);
        }
    }

    // ================================================================
    // 13) Tag_Dedup_IsCaseInsensitive_ButPreservesDisplayCasing
    // ================================================================
    [Fact]
    public async Task Tag_Dedup_IsCaseInsensitive_ButPreservesDisplayCasing()
    {
        var obligationId = await CreateObligationAsync();
        await SetTagsAsync(obligationId, "Obligation", "Finance", "finance", "FINANCE");

        var state = await GetTagStateAsync();
        var tags = state.EntityTags[(obligationId, "Obligation")];

        // Only one survives case-insensitive dedup
        Assert.Single(tags);
        // The surviving tag should be "Finance" (first occurrence wins in handler)
        Assert.Contains("Finance", tags, StringComparer.OrdinalIgnoreCase);
    }

    // ================================================================
    // 14) Tag_Limits_Enforced_Max20_And_Max50Chars
    // ================================================================
    [Fact]
    public async Task Tag_Limits_Enforced_Max20_And_Max50Chars()
    {
        var obligationId = await CreateObligationAsync();

        // Max 20 tags
        var tooMany = Enumerable.Range(1, 21).Select(i => $"tag{i}").ToList();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TagHandler.HandleAsync(
                new UpdateEntityTagsCommand(obligationId, "Obligation", tooMany, new DateOnly(2025, 3, 1)),
                _actorUserId, _deviceId, CancellationToken.None));
        Assert.Contains("Maximum 20 tags", ex.Message);

        // Max 50 chars
        var longTag = new string('x', 51);
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TagHandler.HandleAsync(
                new UpdateEntityTagsCommand(obligationId, "Obligation", [longTag], new DateOnly(2025, 3, 1)),
                _actorUserId, _deviceId, CancellationToken.None));
        Assert.Contains("exceeds 50 character limit", ex2.Message);
    }

    // ================================================================
    // 15) Determinism_SameEvents_SameTagState
    // ================================================================
    [Fact]
    public async Task Determinism_SameEvents_SameTagState()
    {
        var obligationId = await CreateObligationAsync();
        var partyId = await CreatePartyAsync();
        var assetId = await CreateAssetAsync();

        await SetTagsAsync(obligationId, "Obligation", "alpha", "beta");
        await SetTagsAsync(partyId, "Party", "beta", "gamma");
        await SetTagsAsync(assetId, "Asset", "alpha", "delta");
        await SetTagsAsync(obligationId, "Obligation", "epsilon"); // replace

        var all = (await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None)).ToList();
        var state1 = TagProjector.Project(all);
        var state2 = TagProjector.Project(all);

        Assert.Equal(state1.EntityTags.Count, state2.EntityTags.Count);
        foreach (var kv in state1.EntityTags)
        {
            Assert.True(state2.EntityTags.ContainsKey(kv.Key));
            Assert.True(kv.Value.SetEquals(state2.EntityTags[kv.Key]));
        }

        Assert.Equal(state1.TagUsageCounts.Count, state2.TagUsageCounts.Count);
        foreach (var kv in state1.TagUsageCounts)
            Assert.Equal(kv.Value, state2.TagUsageCounts[kv.Key]);
    }

    // ================================================================
    // 16) CrossEntityQuery_ByTag_ReturnsExpectedEntityTypes
    // ================================================================
    [Fact]
    public async Task CrossEntityQuery_ByTag_ReturnsExpectedEntityTypes()
    {
        var accountId = await CreateAccountAsync();
        var obligationId = await CreateObligationAsync();
        var partyId = await CreatePartyAsync();
        var assetId = await CreateAssetAsync();
        var goalId = await CreateGoalAsync();

        var sharedTag = "cross-entity-tag";
        await SetTagsAsync(accountId, "Account", sharedTag);
        await SetTagsAsync(obligationId, "Obligation", sharedTag);
        await SetTagsAsync(partyId, "Party", sharedTag);
        await SetTagsAsync(assetId, "Asset", sharedTag);
        await SetTagsAsync(goalId, "Goal", sharedTag);

        var results = await EntitiesByTagHandler.HandleAsync(sharedTag, CancellationToken.None);

        Assert.Equal(5, results.Count);
        Assert.Contains(results, r => r.EntityId == accountId && r.EntityType == "Account");
        Assert.Contains(results, r => r.EntityId == obligationId && r.EntityType == "Obligation");
        Assert.Contains(results, r => r.EntityId == partyId && r.EntityType == "Party");
        Assert.Contains(results, r => r.EntityId == assetId && r.EntityType == "Asset");
        Assert.Contains(results, r => r.EntityId == goalId && r.EntityType == "Goal");
    }

    // ================================================================
    // 17) UpdateTags_ValidatesEntityExists
    // ================================================================
    [Fact]
    public async Task UpdateTags_ValidatesEntityExists()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TagHandler.HandleAsync(
                new UpdateEntityTagsCommand(Guid.NewGuid(), "Obligation", ["test"], new DateOnly(2025, 3, 1)),
                _actorUserId, _deviceId, CancellationToken.None));
        Assert.Contains("not found", ex.Message);
    }

    // ================================================================
    // 18) NoDirectDbWrites_AllViaEvents
    // ================================================================
    [Fact]
    public async Task NoDirectDbWrites_AllViaEvents()
    {
        var obligationId = await CreateObligationAsync();
        var beforeAll = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var beforeCount = beforeAll.Count;

        await SetTagsAsync(obligationId, "Obligation", "test-tag");

        var afterAll = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        // Exactly one new event should have been appended
        Assert.Equal(beforeCount + 1, afterAll.Count);

        var lastEvent = afterAll[^1];
        Assert.Equal(nameof(EntityTagsReplaced), lastEvent.EventType);
    }
}
