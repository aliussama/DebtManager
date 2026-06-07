using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Services.Serialization;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class TagTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public TagTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"TagTests_{id}.db");
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

    private async Task<Guid> CreateAccountAsync(string name = "Wallet", decimal opening = 5000m)
    {
        var handler = new CreateAccountHandler(_eventStore);
        return await handler.HandleAsync(
            new CreateAccountCommand(null, name, "Cash", opening, "EGP", new DateOnly(2025, 1, 1)),
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

    // ================================================================
    // 1) ReplaceTags_CreatesCorrectState
    // ================================================================
    [Fact]
    public async Task ReplaceTags_CreatesCorrectState()
    {
        var accountId = await CreateAccountAsync();
        var handler = new UpdateEntityTagsHandler(_eventStore);

        await handler.HandleAsync(new UpdateEntityTagsCommand(
            accountId, "Account", ["personal", "savings"], new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state = TagProjector.Project(all);

        Assert.True(state.EntityTags.ContainsKey((accountId, "Account")));
        var tags = state.EntityTags[(accountId, "Account")];
        Assert.Equal(2, tags.Count);
        Assert.Contains("personal", tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("savings", tags, StringComparer.OrdinalIgnoreCase);
    }

    // ================================================================
    // 2) ReplaceTags_RemovesOldTags
    // ================================================================
    [Fact]
    public async Task ReplaceTags_RemovesOldTags()
    {
        var accountId = await CreateAccountAsync();
        var handler = new UpdateEntityTagsHandler(_eventStore);

        // Set initial tags
        await handler.HandleAsync(new UpdateEntityTagsCommand(
            accountId, "Account", ["personal", "savings", "old-tag"], new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Replace with new tags (old-tag removed)
        await handler.HandleAsync(new UpdateEntityTagsCommand(
            accountId, "Account", ["personal", "investment"], new DateOnly(2025, 3, 2)),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state = TagProjector.Project(all);

        var tags = state.EntityTags[(accountId, "Account")];
        Assert.Equal(2, tags.Count);
        Assert.Contains("personal", tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("investment", tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("savings", tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("old-tag", tags, StringComparer.OrdinalIgnoreCase);

        // old-tag and savings should no longer appear in usage counts
        Assert.False(state.TagUsageCounts.ContainsKey("old-tag"));
        Assert.False(state.TagUsageCounts.ContainsKey("savings"));
    }

    // ================================================================
    // 3) Suggestions_AreUsageSorted
    // ================================================================
    [Fact]
    public async Task Suggestions_AreUsageSorted()
    {
        var acc1 = await CreateAccountAsync("A1");
        var acc2 = await CreateAccountAsync("A2");
        var acc3 = await CreateAccountAsync("A3");
        var handler = new UpdateEntityTagsHandler(_eventStore);

        // "common" appears on 3 entities, "rare" on 1
        await handler.HandleAsync(new UpdateEntityTagsCommand(
            acc1, "Account", ["common", "rare"], new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
        await handler.HandleAsync(new UpdateEntityTagsCommand(
            acc2, "Account", ["common", "mid"], new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
        await handler.HandleAsync(new UpdateEntityTagsCommand(
            acc3, "Account", ["common"], new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var suggestionsHandler = new GetTagSuggestionsHandler(_eventStore);
        var suggestions = await suggestionsHandler.HandleAsync(CancellationToken.None);

        Assert.True(suggestions.Count >= 3);
        Assert.Equal("common", suggestions[0].Tag);
        Assert.Equal(3, suggestions[0].UsageCount);
    }

    // ================================================================
    // 4) CrossEntityQuery_ReturnsCorrectEntities
    // ================================================================
    [Fact]
    public async Task CrossEntityQuery_ReturnsCorrectEntities()
    {
        var accountId = await CreateAccountAsync();
        var partyId = await CreatePartyAsync();
        var handler = new UpdateEntityTagsHandler(_eventStore);

        await handler.HandleAsync(new UpdateEntityTagsCommand(
            accountId, "Account", ["shared-tag"], new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
        await handler.HandleAsync(new UpdateEntityTagsCommand(
            partyId, "Party", ["shared-tag", "vendor"], new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var queryHandler = new GetEntitiesByTagHandler(_eventStore);
        var results = await queryHandler.HandleAsync("shared-tag", CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.EntityId == accountId && r.EntityType == "Account");
        Assert.Contains(results, r => r.EntityId == partyId && r.EntityType == "Party");
    }

    // ================================================================
    // 5) CaseInsensitiveBehavior_Works
    // ================================================================
    [Fact]
    public async Task CaseInsensitiveBehavior_Works()
    {
        var accountId = await CreateAccountAsync();
        var handler = new UpdateEntityTagsHandler(_eventStore);

        // Set tags with mixed case
        await handler.HandleAsync(new UpdateEntityTagsCommand(
            accountId, "Account", ["Personal", "SAVINGS"], new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state = TagProjector.Project(all);

        var tags = state.EntityTags[(accountId, "Account")];
        // Case-insensitive lookup
        Assert.Contains("personal", tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("SAVINGS", tags, StringComparer.OrdinalIgnoreCase);

        // Query by different case
        var queryHandler = new GetEntitiesByTagHandler(_eventStore);
        var results = await queryHandler.HandleAsync("personal", CancellationToken.None);
        Assert.Single(results);
        Assert.Equal(accountId, results[0].EntityId);

        results = await queryHandler.HandleAsync("PERSONAL", CancellationToken.None);
        Assert.Single(results);

        // Usage counts are case-insensitive
        Assert.True(state.TagUsageCounts.ContainsKey("personal"));
        Assert.True(state.TagUsageCounts.ContainsKey("PERSONAL")); // same key
    }

    // ================================================================
    // 6) MaxTagLimit_Enforced
    // ================================================================
    [Fact]
    public async Task MaxTagLimit_Enforced()
    {
        var accountId = await CreateAccountAsync();
        var handler = new UpdateEntityTagsHandler(_eventStore);

        var tooManyTags = Enumerable.Range(1, 21).Select(i => $"tag{i}").ToList();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new UpdateEntityTagsCommand(
                accountId, "Account", tooManyTags, new DateOnly(2025, 3, 1)),
                _actorUserId, _deviceId, CancellationToken.None));

        Assert.Contains("Maximum 20 tags", ex.Message);
    }

    // ================================================================
    // 7) MaxTagLength_Enforced
    // ================================================================
    [Fact]
    public async Task MaxTagLength_Enforced()
    {
        var accountId = await CreateAccountAsync();
        var handler = new UpdateEntityTagsHandler(_eventStore);

        var longTag = new string('x', 51);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new UpdateEntityTagsCommand(
                accountId, "Account", [longTag], new DateOnly(2025, 3, 1)),
                _actorUserId, _deviceId, CancellationToken.None));

        Assert.Contains("exceeds 50 character limit", ex.Message);
    }

    // ================================================================
    // 8) EntityValidation_RejectsNonExistentEntity
    // ================================================================
    [Fact]
    public async Task EntityValidation_RejectsNonExistentEntity()
    {
        var handler = new UpdateEntityTagsHandler(_eventStore);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new UpdateEntityTagsCommand(
                Guid.NewGuid(), "Account", ["test"], new DateOnly(2025, 3, 1)),
                _actorUserId, _deviceId, CancellationToken.None));

        Assert.Contains("not found", ex.Message);
    }

    // ================================================================
    // 9) Determinism_SameEventsSameTagState
    // ================================================================
    [Fact]
    public async Task Determinism_SameEventsSameTagState()
    {
        var acc1 = await CreateAccountAsync("A1");
        var acc2 = await CreateAccountAsync("A2");
        var handler = new UpdateEntityTagsHandler(_eventStore);

        await handler.HandleAsync(new UpdateEntityTagsCommand(
            acc1, "Account", ["alpha", "beta"], new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
        await handler.HandleAsync(new UpdateEntityTagsCommand(
            acc2, "Account", ["beta", "gamma"], new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
        // Replace acc1 tags
        await handler.HandleAsync(new UpdateEntityTagsCommand(
            acc1, "Account", ["delta"], new DateOnly(2025, 3, 2)),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = (await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None)).ToList();

        var state1 = TagProjector.Project(all);
        var state2 = TagProjector.Project(all);

        // Same entity tags
        Assert.Equal(state1.EntityTags.Count, state2.EntityTags.Count);
        foreach (var kv in state1.EntityTags)
        {
            Assert.True(state2.EntityTags.ContainsKey(kv.Key));
            Assert.True(kv.Value.SetEquals(state2.EntityTags[kv.Key]));
        }

        // Same usage counts
        Assert.Equal(state1.TagUsageCounts.Count, state2.TagUsageCounts.Count);
        foreach (var kv in state1.TagUsageCounts)
        {
            Assert.Equal(kv.Value, state2.TagUsageCounts[kv.Key]);
        }

        // Verify specific state: acc1 has only "delta", acc2 has "beta"+"gamma"
        Assert.Single(state1.EntityTags[(acc1, "Account")]);
        Assert.Contains("delta", state1.EntityTags[(acc1, "Account")], StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, state1.EntityTags[(acc2, "Account")].Count);

        // alpha should be gone from usage counts
        Assert.False(state1.TagUsageCounts.ContainsKey("alpha"));
        // beta: only acc2 now
        Assert.Equal(1, state1.TagUsageCounts["beta"]);
    }

    // ================================================================
    // 10) EntityTagsReplaced_IsDeserializable
    // ================================================================
    [Fact]
    public async Task EntityTagsReplaced_IsDeserializable()
    {
        var accountId = await CreateAccountAsync();
        var handler = new UpdateEntityTagsHandler(_eventStore);

        await handler.HandleAsync(new UpdateEntityTagsCommand(
            accountId, "Account", ["test-tag"], new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);

        var tagEnvelopes = all.Where(e => e.EventType == nameof(EntityTagsReplaced)).ToList();
        Assert.Single(tagEnvelopes);

        // Verify deserialization via EnvelopeDeserializer
        var domainEvents = EnvelopeDeserializer.ToDomainEvents(all).ToList();
        var tagEvents = domainEvents.OfType<EntityTagsReplaced>().ToList();
        Assert.Single(tagEvents);
        Assert.Equal(accountId, tagEvents[0].EntityId);
        Assert.Equal("Account", tagEvents[0].EntityType);
        Assert.Single(tagEvents[0].Tags);
        Assert.Equal("test-tag", tagEvents[0].Tags[0]);
    }

    // ================================================================
    // 11) CaseInsensitiveDuplicates_AreDeduped
    // ================================================================
    [Fact]
    public async Task CaseInsensitiveDuplicates_AreDeduped()
    {
        var accountId = await CreateAccountAsync();
        var handler = new UpdateEntityTagsHandler(_eventStore);

        // "Test" and "test" should be deduped
        await handler.HandleAsync(new UpdateEntityTagsCommand(
            accountId, "Account", ["Test", "test", "TEST"], new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state = TagProjector.Project(all);

        var tags = state.EntityTags[(accountId, "Account")];
        Assert.Single(tags); // only one survives dedup
    }

    // ================================================================
    // 12) EmptyTagsReplace_ClearsAllTags
    // ================================================================
    [Fact]
    public async Task EmptyTagsReplace_ClearsAllTags()
    {
        var accountId = await CreateAccountAsync();
        var handler = new UpdateEntityTagsHandler(_eventStore);

        await handler.HandleAsync(new UpdateEntityTagsCommand(
            accountId, "Account", ["alpha", "beta"], new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Replace with empty
        await handler.HandleAsync(new UpdateEntityTagsCommand(
            accountId, "Account", [], new DateOnly(2025, 3, 2)),
            _actorUserId, _deviceId, CancellationToken.None);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state = TagProjector.Project(all);

        var tags = state.EntityTags[(accountId, "Account")];
        Assert.Empty(tags);

        // Usage counts for alpha and beta should be 0 (removed)
        Assert.False(state.TagUsageCounts.ContainsKey("alpha"));
        Assert.False(state.TagUsageCounts.ContainsKey("beta"));
    }
}
