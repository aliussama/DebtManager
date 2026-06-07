using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Fx;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class MultiCurrencyFxGraphTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public MultiCurrencyFxGraphTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"FxGraphTests_{id}.db");
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

    // ================================================================
    // 1) FxGraph_DirectRate_NearestBefore_Works
    // ================================================================
    [Fact]
    public void FxGraph_DirectRate_NearestBefore_Works()
    {
        var rates = new List<FxRatePoint>
        {
            MakeRate("USD", "EGP", new DateOnly(2025, 1, 10), 50m),
            MakeRate("USD", "EGP", new DateOnly(2025, 1, 20), 51m),
        };

        var graph = FxGraph.Build(rates);
        var config = new FxPolicyConfig(FxValuationPolicy.NearestBefore, 14);

        // Query on Jan 15: should get Jan 10 rate (50)
        Assert.True(graph.TryGetRate("USD", "EGP", new DateOnly(2025, 1, 15), config, out var rate, out var meta));
        Assert.Equal(50m, rate);
        Assert.True(meta.IsKnown);
        Assert.Equal("USD>EGP", meta.Path);
        Assert.Equal(1, meta.Hops);
        Assert.Equal(new DateOnly(2025, 1, 10), meta.RateDateUsed);

        // Query on Jan 25: should get Jan 20 rate (51)
        Assert.True(graph.TryGetRate("USD", "EGP", new DateOnly(2025, 1, 25), config, out var rate2, out _));
        Assert.Equal(51m, rate2);
    }

    // ================================================================
    // 2) FxGraph_InverseRate_Works
    // ================================================================
    [Fact]
    public void FxGraph_InverseRate_Works()
    {
        // Only USD->EGP recorded, query EGP->USD
        var rates = new List<FxRatePoint>
        {
            MakeRate("USD", "EGP", new DateOnly(2025, 1, 10), 50m),
        };

        var graph = FxGraph.Build(rates);
        var config = new FxPolicyConfig(FxValuationPolicy.NearestBefore, 14);

        Assert.True(graph.TryGetRate("EGP", "USD", new DateOnly(2025, 1, 15), config, out var rate, out var meta));
        Assert.Equal(1m / 50m, rate);
        Assert.True(meta.IsKnown);
        Assert.Equal("EGP>USD", meta.Path);
    }

    // ================================================================
    // 3) FxGraph_MultiHopPath_Works_AndIsDeterministic
    // ================================================================
    [Fact]
    public void FxGraph_MultiHopPath_Works_AndIsDeterministic()
    {
        // EGP->USD->EUR (no direct EGP->EUR)
        var rates = new List<FxRatePoint>
        {
            MakeRate("EGP", "USD", new DateOnly(2025, 1, 10), 0.02m),  // 1 EGP = 0.02 USD
            MakeRate("USD", "EUR", new DateOnly(2025, 1, 10), 0.92m),  // 1 USD = 0.92 EUR
        };

        var graph = FxGraph.Build(rates);
        var config = new FxPolicyConfig(FxValuationPolicy.NearestBefore, 14);

        Assert.True(graph.TryGetRate("EGP", "EUR", new DateOnly(2025, 1, 15), config, out var rate, out var meta));
        Assert.Equal(0.02m * 0.92m, rate);
        Assert.True(meta.IsKnown);
        Assert.Equal(2, meta.Hops);
        Assert.Contains("EGP", meta.Path);
        Assert.Contains("EUR", meta.Path);

        // Run again — deterministic
        Assert.True(graph.TryGetRate("EGP", "EUR", new DateOnly(2025, 1, 15), config, out var rate2, out var meta2));
        Assert.Equal(rate, rate2);
        Assert.Equal(meta.Path, meta2.Path);
    }

    // ================================================================
    // 4) FxGraph_PathTieBreak_Lexicographic_Works
    // ================================================================
    [Fact]
    public void FxGraph_PathTieBreak_Lexicographic_Works()
    {
        // Two 2-hop paths: EGP->GBP->JPY and EGP->USD->JPY
        // Lexicographic: EGP>GBP>JPY < EGP>USD>JPY
        var rates = new List<FxRatePoint>
        {
            MakeRate("EGP", "GBP", new DateOnly(2025, 1, 10), 0.016m),
            MakeRate("GBP", "JPY", new DateOnly(2025, 1, 10), 190m),
            MakeRate("EGP", "USD", new DateOnly(2025, 1, 10), 0.02m),
            MakeRate("USD", "JPY", new DateOnly(2025, 1, 10), 150m),
        };

        var graph = FxGraph.Build(rates);
        var config = new FxPolicyConfig(FxValuationPolicy.NearestBefore, 14);

        Assert.True(graph.TryGetRate("EGP", "JPY", new DateOnly(2025, 1, 15), config, out var rate, out var meta));
        // Should pick EGP>GBP>JPY (lexicographically smaller)
        Assert.Equal("EGP>GBP>JPY", meta.Path);
        Assert.Equal(0.016m * 190m, rate);
    }

    // ================================================================
    // 5) FxPolicy_Spot_RequiresExactDate
    // ================================================================
    [Fact]
    public void FxPolicy_Spot_RequiresExactDate()
    {
        var rates = new List<FxRatePoint>
        {
            MakeRate("USD", "EGP", new DateOnly(2025, 1, 10), 50m),
        };

        var graph = FxGraph.Build(rates);
        var config = new FxPolicyConfig(FxValuationPolicy.Spot, 14);

        // Exact date match
        Assert.True(graph.TryGetRate("USD", "EGP", new DateOnly(2025, 1, 10), config, out var rate, out _));
        Assert.Equal(50m, rate);

        // Different date — should fail
        Assert.False(graph.TryGetRate("USD", "EGP", new DateOnly(2025, 1, 11), config, out _, out var meta));
        Assert.False(meta.IsKnown);
    }

    // ================================================================
    // 6) FxPolicy_MaxAgeDays_RejectsTooOldRate
    // ================================================================
    [Fact]
    public void FxPolicy_MaxAgeDays_RejectsTooOldRate()
    {
        var rates = new List<FxRatePoint>
        {
            MakeRate("USD", "EGP", new DateOnly(2025, 1, 1), 50m),
        };

        var graph = FxGraph.Build(rates);

        // MaxAgeDays=5 — query on Jan 10 is 9 days away, too old
        var strictConfig = new FxPolicyConfig(FxValuationPolicy.NearestBefore, 5);
        Assert.False(graph.TryGetRate("USD", "EGP", new DateOnly(2025, 1, 10), strictConfig, out _, out _));

        // MaxAgeDays=14 — query on Jan 10 is within range
        var relaxedConfig = new FxPolicyConfig(FxValuationPolicy.NearestBefore, 14);
        Assert.True(graph.TryGetRate("USD", "EGP", new DateOnly(2025, 1, 10), relaxedConfig, out var rate, out _));
        Assert.Equal(50m, rate);
    }

    // ================================================================
    // 7) CurrencySettings_SetAndProject_Works
    // ================================================================
    [Fact]
    public async Task CurrencySettings_SetAndProject_Works()
    {
        var getHandler = new GetCurrencySettingsHandler(_eventStore);
        var setHandler = new SetReportingCurrencyHandler(_eventStore);
        var policyHandler = new SetFxPolicyHandler(_eventStore);

        // Default state
        var initial = await getHandler.HandleAsync(CancellationToken.None);
        Assert.Equal("EGP", initial.ReportingCurrencyCode);
        Assert.False(initial.IsConfigured);
        Assert.Equal(FxValuationPolicy.NearestBefore, initial.Policy);

        // Set currency
        await setHandler.HandleAsync(new SetReportingCurrencyCommand("USD"), _actorUserId, _deviceId, CancellationToken.None);
        var afterCurrency = await getHandler.HandleAsync(CancellationToken.None);
        Assert.Equal("USD", afterCurrency.ReportingCurrencyCode);
        Assert.True(afterCurrency.IsConfigured);

        // Set policy
        await policyHandler.HandleAsync(
            new SetFxPolicyCommand(FxValuationPolicy.Spot, 7),
            _actorUserId, _deviceId, CancellationToken.None);
        var afterPolicy = await getHandler.HandleAsync(CancellationToken.None);
        Assert.Equal(FxValuationPolicy.Spot, afterPolicy.Policy);
        Assert.Equal(7, afterPolicy.MaxAgeDays);
        Assert.Equal("USD", afterPolicy.ReportingCurrencyCode); // Currency preserved
    }

    // ================================================================
    // 8) NetWorth_UsesReportingCurrency_AndFlagsUnknownFx
    // ================================================================
    [Fact]
    public async Task NetWorth_UsesReportingCurrency_AndFlagsUnknownFx()
    {
        // Create EGP account
        var accountId = Guid.NewGuid();
        await AppendAccountCreated(accountId, "Wallet", 5000m, "EGP");

        // Create USD account
        var usdAccountId = Guid.NewGuid();
        await AppendAccountCreated(usdAccountId, "USD Wallet", 200m, "USD");

        // Record FX rate: USD->EGP = 50
        await AppendFxRate("USD", "EGP", new DateOnly(2025, 1, 10), 50m);

        // Report in EGP
        var dashboardHandler = new GetPortfolioDashboardHandler(_eventStore);
        var obligationsHandler = new GetObligationsListHandler(dashboardHandler);
        var netWorthHandler = new GetNetWorthReportHandler(_eventStore, obligationsHandler);

        var report = await netWorthHandler.HandleAsync(
            new GetNetWorthReportQuery(new DateOnly(2025, 1, 15), "EGP"),
            CancellationToken.None);

        // EGP account = 5000 EGP, USD account = 200 * 50 = 10000 EGP
        Assert.Equal(15000m, report.TotalCash);
        Assert.Equal(0, report.UnknownValueCount);

        // Now report in JPY (no FX rate for JPY)
        var reportJpy = await netWorthHandler.HandleAsync(
            new GetNetWorthReportQuery(new DateOnly(2025, 1, 15), "JPY"),
            CancellationToken.None);

        // Both accounts have unknown FX
        Assert.Equal(2, reportJpy.UnknownValueCount);
        Assert.Equal(0m, reportJpy.TotalCash);
        Assert.All(reportJpy.Rows, r => Assert.False(r.IsValued));
    }

    // ================================================================
    // 9) TaxReport_UsesSamePolicyConfig_AsSettings
    // ================================================================
    [Fact]
    public async Task TaxReport_UsesSamePolicyConfig_AsSettings()
    {
        // Create tax profile
        var profileHandler = new CreateTaxProfileHandler(_eventStore);
        var reportHandler = new GetTaxYearReportHandler(_eventStore);

        var profileId = await profileHandler.HandleAsync(
            new CreateTaxProfileCommand(null, "Personal", "EG", 1, 1, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Set currency settings
        var setHandler = new SetReportingCurrencyHandler(_eventStore);
        await setHandler.HandleAsync(new SetReportingCurrencyCommand("EGP"), _actorUserId, _deviceId, CancellationToken.None);

        var policyHandler = new SetFxPolicyHandler(_eventStore);
        await policyHandler.HandleAsync(
            new SetFxPolicyCommand(FxValuationPolicy.NearestBefore, 14),
            _actorUserId, _deviceId, CancellationToken.None);

        // Generate report — should not throw, should produce valid output
        var report = await reportHandler.HandleAsync(profileId, 2025, CancellationToken.None);
        Assert.Equal(profileId, report.ProfileId);
        Assert.Equal(2025, report.TaxYear);
        Assert.Equal("EGP", report.BaseCurrency);
    }

    // ================================================================
    // 10) SnapshotRunner_Compatibility
    // ================================================================
    [Fact]
    public async Task SnapshotRunner_Compatibility_CurrencySettingsEventsDoNotBreakProjections()
    {
        // Create some cash data
        var accountId = Guid.NewGuid();
        await AppendAccountCreated(accountId, "Test Account", 10000m, "EGP");

        // Set currency settings (these events go through the event store)
        var setHandler = new SetReportingCurrencyHandler(_eventStore);
        await setHandler.HandleAsync(new SetReportingCurrencyCommand("USD"), _actorUserId, _deviceId, CancellationToken.None);

        // Verify cash ledger still works (currency settings events should be ignored by CashLedgerProjector)
        var allEnvelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var cashState = CashLedgerProjector.Project(allEnvelopes);

        Assert.True(cashState.Accounts.ContainsKey(accountId));
        Assert.Equal(10000m, cashState.Accounts[accountId].Balance);

        // Verify currency settings were projected correctly
        var settingsState = CurrencySettingsProjector.Project(allEnvelopes);
        Assert.Equal("USD", settingsState.ReportingCurrencyCode);
        Assert.True(settingsState.IsConfigured);
    }

    // ================================================================
    // 11) FxGraph_SameCurrency_ReturnsIdentity
    // ================================================================
    [Fact]
    public void FxGraph_SameCurrency_ReturnsIdentity()
    {
        var graph = FxGraph.Build(new List<FxRatePoint>());
        var config = FxPolicyConfig.Default;

        Assert.True(graph.TryGetRate("EGP", "EGP", new DateOnly(2025, 1, 1), config, out var rate, out var meta));
        Assert.Equal(1m, rate);
        Assert.True(meta.IsKnown);
        Assert.Equal(0, meta.Hops);
    }

    // ================================================================
    // 12) FxGraph_NearestPolicy_PrefersClosest
    // ================================================================
    [Fact]
    public void FxGraph_NearestPolicy_PrefersClosest()
    {
        var rates = new List<FxRatePoint>
        {
            MakeRate("USD", "EGP", new DateOnly(2025, 1, 5), 49m),
            MakeRate("USD", "EGP", new DateOnly(2025, 1, 15), 51m),
        };

        var graph = FxGraph.Build(rates);
        var config = new FxPolicyConfig(FxValuationPolicy.Nearest, 14);

        // Query Jan 11 — Jan 15 is 4 days away, Jan 5 is 6 days away
        Assert.True(graph.TryGetRate("USD", "EGP", new DateOnly(2025, 1, 11), config, out var rate, out _));
        Assert.Equal(51m, rate); // Closer to Jan 15
    }

    // ================================================================
    // 13) CurrencySettings_Validation_RejectsInvalid
    // ================================================================
    [Fact]
    public async Task CurrencySettings_Validation_RejectsInvalid()
    {
        var setHandler = new SetReportingCurrencyHandler(_eventStore);
        var policyHandler = new SetFxPolicyHandler(_eventStore);

        // Invalid currency code
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            setHandler.HandleAsync(new SetReportingCurrencyCommand("x"), _actorUserId, _deviceId, CancellationToken.None));

        // MaxAgeDays out of range
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policyHandler.HandleAsync(new SetFxPolicyCommand(FxValuationPolicy.NearestBefore, -1),
                _actorUserId, _deviceId, CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policyHandler.HandleAsync(new SetFxPolicyCommand(FxValuationPolicy.NearestBefore, 9999),
                _actorUserId, _deviceId, CancellationToken.None));
    }

    // --- Helpers ---

    private static FxRatePoint MakeRate(string from, string to, DateOnly date, decimal rate)
    {
        return new FxRatePoint
        {
            RateId = Guid.NewGuid(),
            FromCurrencyCode = from,
            ToCurrencyCode = to,
            AsOfDate = date,
            Rate = rate,
            Source = "Test"
        };
    }

    private async Task AppendAccountCreated(Guid accountId, string name, decimal openingBalance, string currencyCode)
    {
        var ev = new AccountCreated(accountId, name, "Cash", currencyCode, openingBalance,
            new DateOnly(2025, 1, 1));
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(accountId),
            nameof(AccountCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            _actorUserId, _deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);
    }

    private async Task AppendFxRate(string from, string to, DateOnly date, decimal rate)
    {
        var rateId = Guid.NewGuid();
        var streamId = DeterministicGuid(from + ":" + to);
        var ev = new FxRateRecorded(rateId, from, to, date, rate, "Test", "");
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(streamId),
            nameof(FxRateRecorded), DateTimeOffset.UtcNow, date,
            _actorUserId, _deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);
    }

    private static Guid DeterministicGuid(string key)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(key.ToUpperInvariant());
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return new Guid(hash.AsSpan(0, 16));
    }
}
