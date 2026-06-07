using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class PortfolioInvestmentsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public PortfolioInvestmentsTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"PortfolioInvestmentsTests_{id}.db");
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
            catch (IOException) when (i < 29)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Fact]
    public async Task CreateInvestmentAccount_Works()
    {
        // Arrange
        var createHandler = new CreateInvestmentAccountHandler(_eventStore);
        var dashboardHandler = new GetInvestmentPortfolioDashboardHandler(_eventStore);

        // Act
        var accountId = await createHandler.HandleAsync(
            new CreateInvestmentAccountCommand(null, "My Brokerage", "USD", "Interactive Brokers",
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var dashboard = await dashboardHandler.HandleAsync(ct: CancellationToken.None);

        // Assert
        Assert.Single(dashboard.Accounts);
        var account = dashboard.Accounts[0];
        Assert.Equal("My Brokerage", account.Name);
        Assert.Equal("USD", account.CurrencyCode);
        Assert.Equal("Interactive Brokers", account.BrokerName);
        Assert.False(account.IsArchived);
        Assert.Equal("FIFO", account.CostBasisMode);
    }

    [Fact]
    public async Task RecordBuy_IncreasesPosition_AndCostBasis()
    {
        // Arrange
        var createAcctHandler = new CreateInvestmentAccountHandler(_eventStore);
        var txnHandler = new RecordInvestmentTransactionHandler(_eventStore);
        var dashboardHandler = new GetInvestmentPortfolioDashboardHandler(_eventStore);

        var accountId = await createAcctHandler.HandleAsync(
            new CreateInvestmentAccountCommand(null, "Brokerage", "USD", "Broker",
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Create asset for price lookup
        var assetId = await CreateAsset("AAPL", "USD");

        // Act: Buy 10 AAPL at $150 with $5 fees
        await txnHandler.HandleAsync(
            new RecordInvestmentTransactionCommand(null, accountId, assetId, "AAPL",
                "Buy", new DateOnly(2025, 1, 15), null,
                10m, 150m, 5m, 0m, "USD", null, "First buy", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        // Record price for valuation
        await RecordPrice(assetId, new DateOnly(2025, 1, 15), 150m, "USD");

        var dashboard = await dashboardHandler.HandleAsync(new DateOnly(2025, 2, 1), CancellationToken.None);

        // Assert
        Assert.Single(dashboard.Positions);
        var pos = dashboard.Positions[0];
        Assert.Equal("AAPL", pos.Symbol);
        Assert.Equal(10m, pos.Quantity);
        // Total cost = 10 * 150 + 5 = 1505
        Assert.Equal(1505m, pos.TotalCost);
        // Avg cost = 1505 / 10 = 150.5
        Assert.Equal(150.5m, pos.AvgCost);
    }

    [Fact]
    public async Task RecordSell_FifoRealizedPnL_Correct()
    {
        // Arrange
        var createAcctHandler = new CreateInvestmentAccountHandler(_eventStore);
        var txnHandler = new RecordInvestmentTransactionHandler(_eventStore);
        var dashboardHandler = new GetInvestmentPortfolioDashboardHandler(_eventStore);
        var holdingHandler = new GetHoldingDetailHandler(_eventStore);

        var accountId = await createAcctHandler.HandleAsync(
            new CreateInvestmentAccountCommand(null, "Brokerage", "USD", "Broker",
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var assetId = await CreateAsset("AAPL", "USD");

        // Buy 10 at $100 (total cost = 1000 + 0 fees = 1000)
        await txnHandler.HandleAsync(
            new RecordInvestmentTransactionCommand(null, accountId, assetId, "AAPL",
                "Buy", new DateOnly(2025, 1, 10), null,
                10m, 100m, 0m, 0m, "USD", null, "", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        // Buy 10 at $120 (total cost = 1200 + 0 = 1200)
        await txnHandler.HandleAsync(
            new RecordInvestmentTransactionCommand(null, accountId, assetId, "AAPL",
                "Buy", new DateOnly(2025, 2, 10), null,
                10m, 120m, 0m, 0m, "USD", null, "", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        // Act: Sell 15 at $130 (FIFO: 10 from $100 lot, 5 from $120 lot)
        await txnHandler.HandleAsync(
            new RecordInvestmentTransactionCommand(null, accountId, assetId, "AAPL",
                "Sell", new DateOnly(2025, 3, 10), null,
                15m, 130m, 0m, 0m, "USD", null, "", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        var detail = await holdingHandler.HandleAsync(accountId, assetId, new DateOnly(2025, 3, 15), CancellationToken.None);

        // Assert
        Assert.NotNull(detail);

        // Remaining: 5 shares from $120 lot
        Assert.Equal(5m, detail!.Quantity);

        // Cost basis of sold: 10 * 100 + 5 * 120 = 1600
        // Proceeds: 15 * 130 = 1950
        // Realized gain: 1950 - 1600 = 350
        var pnl = detail.RealizedPnLEntries;
        Assert.Single(pnl);
        Assert.Equal(1950m, pnl[0].Proceeds);
        Assert.Equal(1600m, pnl[0].CostBasis);
        Assert.Equal(350m, pnl[0].RealizedGain);
    }

    [Fact]
    public async Task AverageCostMode_SellRealizedPnL_Correct()
    {
        // Arrange
        var createAcctHandler = new CreateInvestmentAccountHandler(_eventStore);
        var txnHandler = new RecordInvestmentTransactionHandler(_eventStore);
        var modeHandler = new SetCostBasisModeHandler(_eventStore);
        var holdingHandler = new GetHoldingDetailHandler(_eventStore);

        var accountId = await createAcctHandler.HandleAsync(
            new CreateInvestmentAccountCommand(null, "AvgCost Account", "USD", "Broker",
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Set average cost mode
        await modeHandler.HandleAsync(
            new SetCostBasisModeCommand(accountId, "AverageCost", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var assetId = await CreateAsset("MSFT", "USD");

        // Buy 10 at $200 (cost = 2000)
        await txnHandler.HandleAsync(
            new RecordInvestmentTransactionCommand(null, accountId, assetId, "MSFT",
                "Buy", new DateOnly(2025, 1, 10), null,
                10m, 200m, 0m, 0m, "USD", null, "", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        // Buy 10 at $300 (cost = 3000)
        await txnHandler.HandleAsync(
            new RecordInvestmentTransactionCommand(null, accountId, assetId, "MSFT",
                "Buy", new DateOnly(2025, 2, 10), null,
                10m, 300m, 0m, 0m, "USD", null, "", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        // Average cost = (2000 + 3000) / 20 = 250

        // Act: Sell 5 at $350
        await txnHandler.HandleAsync(
            new RecordInvestmentTransactionCommand(null, accountId, assetId, "MSFT",
                "Sell", new DateOnly(2025, 3, 10), null,
                5m, 350m, 0m, 0m, "USD", null, "", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        var detail = await holdingHandler.HandleAsync(accountId, assetId, new DateOnly(2025, 3, 15), CancellationToken.None);

        // Assert
        Assert.NotNull(detail);
        Assert.Equal(15m, detail!.Quantity);

        // Cost basis = 5 * 250 = 1250
        // Proceeds = 5 * 350 = 1750
        // Realized = 1750 - 1250 = 500
        var pnl = detail.RealizedPnLEntries;
        Assert.Single(pnl);
        Assert.Equal(1750m, pnl[0].Proceeds);
        Assert.Equal(1250m, pnl[0].CostBasis);
        Assert.Equal(500m, pnl[0].RealizedGain);
    }

    [Fact]
    public async Task Split_AdjustsQuantity_NoValueCreation()
    {
        // Arrange
        var createAcctHandler = new CreateInvestmentAccountHandler(_eventStore);
        var txnHandler = new RecordInvestmentTransactionHandler(_eventStore);
        var holdingHandler = new GetHoldingDetailHandler(_eventStore);

        var accountId = await createAcctHandler.HandleAsync(
            new CreateInvestmentAccountCommand(null, "Brokerage", "USD", "Broker",
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var assetId = await CreateAsset("TSLA", "USD");

        // Buy 10 at $1000 (cost = 10000)
        await txnHandler.HandleAsync(
            new RecordInvestmentTransactionCommand(null, accountId, assetId, "TSLA",
                "Buy", new DateOnly(2025, 1, 10), null,
                10m, 1000m, 0m, 0m, "USD", null, "", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        // Act: 2-for-1 split (Quantity = 2 means split ratio)
        await txnHandler.HandleAsync(
            new RecordInvestmentTransactionCommand(null, accountId, assetId, "TSLA",
                "Split", new DateOnly(2025, 2, 1), null,
                2m, 0m, 0m, 0m, "USD", null, "2-for-1 split", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        var detail = await holdingHandler.HandleAsync(accountId, assetId, new DateOnly(2025, 2, 15), CancellationToken.None);

        // Assert
        Assert.NotNull(detail);
        // Quantity: 10 * 2 = 20
        Assert.Equal(20m, detail!.Quantity);
        // Total cost stays the same: 10000
        Assert.Equal(10000m, detail.TotalCost);
        // Avg cost: 10000 / 20 = 500
        Assert.Equal(500m, detail.AvgCost);
    }

    [Fact]
    public async Task MultiCurrencyTrade_UsesFxRateRecorded_ForReportingCurrency()
    {
        // Arrange
        var createAcctHandler = new CreateInvestmentAccountHandler(_eventStore);
        var txnHandler = new RecordInvestmentTransactionHandler(_eventStore);
        var fxHandler = new RecordFxRateHandler(_eventStore);
        var dashboardHandler = new GetInvestmentPortfolioDashboardHandler(_eventStore);

        var accountId = await createAcctHandler.HandleAsync(
            new CreateInvestmentAccountCommand(null, "Multi-Ccy Brokerage", "USD", "Broker",
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var assetId = await CreateAsset("BMW.DE", "EUR");

        // Buy 10 BMW.DE at EUR 100 (with FX rate 1 EUR = 1.1 USD)
        await txnHandler.HandleAsync(
            new RecordInvestmentTransactionCommand(null, accountId, assetId, "BMW.DE",
                "Buy", new DateOnly(2025, 1, 15), null,
                10m, 100m, 0m, 0m, "EUR", 1.1m, "EUR buy", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        // Record FX rate
        await fxHandler.HandleAsync(
            new RecordFxRateCommand(null, "EUR", "USD", new DateOnly(2025, 1, 15), 1.1m, "Manual", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        // Record price in EUR
        await RecordPrice(assetId, new DateOnly(2025, 1, 20), 110m, "EUR");

        var dashboard = await dashboardHandler.HandleAsync(new DateOnly(2025, 1, 25), CancellationToken.None);

        // Assert: position exists with 10 shares
        Assert.Single(dashboard.Positions);
        var pos = dashboard.Positions[0];
        Assert.Equal(10m, pos.Quantity);
        // Cost = 10 * 100 = 1000 EUR
        Assert.Equal(1000m, pos.TotalCost);
        // Market price from AssetPriceRecorded = 110 EUR
        Assert.Equal(110m, pos.MarketPrice);
        // Market value = 10 * 110 = 1100 EUR
        Assert.Equal(1100m, pos.MarketValue);
        Assert.True(pos.IsValued);
    }

    [Fact]
    public async Task ReverseTransaction_RollsBackPositionAndPnL()
    {
        // Arrange
        var createAcctHandler = new CreateInvestmentAccountHandler(_eventStore);
        var txnHandler = new RecordInvestmentTransactionHandler(_eventStore);
        var reverseHandler = new ReverseInvestmentTransactionHandler(_eventStore);
        var dashboardHandler = new GetInvestmentPortfolioDashboardHandler(_eventStore);

        var accountId = await createAcctHandler.HandleAsync(
            new CreateInvestmentAccountCommand(null, "Brokerage", "USD", "Broker",
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var assetId = await CreateAsset("GOOG", "USD");

        // Buy 10 at $100
        var buyTxnId = await txnHandler.HandleAsync(
            new RecordInvestmentTransactionCommand(null, accountId, assetId, "GOOG",
                "Buy", new DateOnly(2025, 1, 10), null,
                10m, 100m, 0m, 0m, "USD", null, "", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        // Act: Reverse the buy
        await reverseHandler.HandleAsync(
            new ReverseInvestmentTransactionCommand(buyTxnId, new DateOnly(2025, 1, 15), "Entered in error"),
            _actorUserId, _deviceId, CancellationToken.None);

        var dashboard = await dashboardHandler.HandleAsync(new DateOnly(2025, 2, 1), CancellationToken.None);

        // Assert: no positions (reversed)
        Assert.Empty(dashboard.Positions.Where(p => p.Quantity > 0));
        Assert.Equal(0m, dashboard.TotalCostBasis);
        Assert.Equal(0m, dashboard.TotalRealizedPnL);
    }

    [Fact]
    public async Task MissingPrice_FlagsUnvaluedButKeepsQuantity()
    {
        // Arrange
        var createAcctHandler = new CreateInvestmentAccountHandler(_eventStore);
        var txnHandler = new RecordInvestmentTransactionHandler(_eventStore);
        var dashboardHandler = new GetInvestmentPortfolioDashboardHandler(_eventStore);

        var accountId = await createAcctHandler.HandleAsync(
            new CreateInvestmentAccountCommand(null, "Brokerage", "USD", "Broker",
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var assetId = await CreateAsset("PRIV", "USD");

        // Buy 100 shares (no price recorded for this asset)
        await txnHandler.HandleAsync(
            new RecordInvestmentTransactionCommand(null, accountId, assetId, "PRIV",
                "Buy", new DateOnly(2025, 1, 15), null,
                100m, 50m, 0m, 0m, "USD", null, "", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        // Act: no price recorded!
        var dashboard = await dashboardHandler.HandleAsync(new DateOnly(2025, 2, 1), CancellationToken.None);

        // Assert
        Assert.Single(dashboard.Positions);
        var pos = dashboard.Positions[0];
        Assert.Equal(100m, pos.Quantity);
        Assert.Equal(5000m, pos.TotalCost);
        Assert.False(pos.IsValued);
        Assert.Null(pos.MarketPrice);
        Assert.Null(pos.MarketValue);
        Assert.Null(pos.UnrealizedPnL);
        Assert.Equal(1, dashboard.UnvaluedPositionCount);
        Assert.Equal(0m, dashboard.TotalMarketValue);
    }

    // --- Helpers ---

    private async Task<Guid> CreateAsset(string symbol, string currencyCode)
    {
        var assetId = Guid.NewGuid();
        var qtySpec = JsonSerializer.Serialize(new { symbol, units = 0m });
        var ev = new AssetCreated(assetId, symbol, "SecurityHolding", currencyCode,
            qtySpec, [], "", DateOnly.FromDateTime(DateTime.Today));
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(assetId),
            nameof(AssetCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            _actorUserId, _deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);
        return assetId;
    }

    private async Task RecordPrice(Guid assetId, DateOnly asOfDate, decimal price, string currencyCode)
    {
        var ev = new AssetPriceRecorded(Guid.NewGuid(), assetId, asOfDate, price, currencyCode, "Manual", "");
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(assetId),
            nameof(AssetPriceRecorded), DateTimeOffset.UtcNow, asOfDate,
            _actorUserId, _deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);
    }
}
