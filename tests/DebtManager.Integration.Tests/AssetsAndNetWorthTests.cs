using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class AssetsAndNetWorthTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public AssetsAndNetWorthTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"AssetsNetWorthTests_{id}.db");
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
    public async Task Assets_CreateAndList_Works()
    {
        // Arrange
        var createHandler = new CreateAssetHandler(_eventStore);
        var listHandler = new GetAssetsListHandler(_eventStore);

        var qtySpec = JsonSerializer.Serialize(new { unit = "property", amount = 1m });

        // Act
        var assetId = await createHandler.HandleAsync(
            new CreateAssetCommand(null, "Downtown Apartment", "RealEstate", "EGP",
                qtySpec, new[] { "cairo", "rental" }, "2BR apartment", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var assets = await listHandler.HandleAsync(ct: CancellationToken.None);

        // Assert
        Assert.Single(assets);
        var asset = assets[0];
        Assert.Equal("Downtown Apartment", asset.Name);
        Assert.Equal("RealEstate", asset.AssetType);
        Assert.Equal("EGP", asset.NativeCurrencyCode);
        Assert.Equal(1m, asset.Quantity);
        Assert.False(asset.IsArchived);
        Assert.Null(asset.LatestPrice);
    }

    [Fact]
    public async Task AssetPriceRecording_UpdatesValuation_AsOfDate()
    {
        // Arrange
        var createHandler = new CreateAssetHandler(_eventStore);
        var priceHandler = new RecordAssetPriceHandler(_eventStore);
        var listHandler = new GetAssetsListHandler(_eventStore);

        var qtySpec = JsonSerializer.Serialize(new { unit = "grams", amount = 100m });

        var assetId = await createHandler.HandleAsync(
            new CreateAssetCommand(null, "Gold Bars", "PreciousMetal", "USD",
                qtySpec, [], "100g of gold", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Record price as-of Jan 15
        await priceHandler.HandleAsync(
            new RecordAssetPriceCommand(null, assetId, new DateOnly(2025, 1, 15), 60m, "USD", "Manual", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        // Record a later price as-of Feb 1
        await priceHandler.HandleAsync(
            new RecordAssetPriceCommand(null, assetId, new DateOnly(2025, 2, 1), 65m, "USD", "Manual", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        // Act: query as-of Jan 20 — should get Jan 15 price
        var assetsJan = await listHandler.HandleAsync(new DateOnly(2025, 1, 20), CancellationToken.None);
        var goldJan = assetsJan.Single(a => a.AssetId == assetId);

        // Act: query as-of Feb 5 — should get Feb 1 price
        var assetsFeb = await listHandler.HandleAsync(new DateOnly(2025, 2, 5), CancellationToken.None);
        var goldFeb = assetsFeb.Single(a => a.AssetId == assetId);

        // Assert
        Assert.Equal(60m, goldJan.LatestPrice);
        Assert.Equal(65m, goldFeb.LatestPrice);
    }

    [Fact]
    public async Task FxRate_ConvertsValues_IntoReportingCurrency()
    {
        // Arrange
        var createHandler = new CreateAssetHandler(_eventStore);
        var priceHandler = new RecordAssetPriceHandler(_eventStore);
        var fxHandler = new RecordFxRateHandler(_eventStore);
        var dashboardHandler = CreateDashboardHandler();
        var obligationsHandler = new GetObligationsListHandler(dashboardHandler);
        var netWorthHandler = new GetNetWorthReportHandler(_eventStore, obligationsHandler);

        var qtySpec = JsonSerializer.Serialize(new { unit = "ounces", amount = 10m });
        var assetId = await createHandler.HandleAsync(
            new CreateAssetCommand(null, "Gold Coins", "PreciousMetal", "USD",
                qtySpec, [], "", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Price gold at $2000/oz in USD
        await priceHandler.HandleAsync(
            new RecordAssetPriceCommand(null, assetId, new DateOnly(2025, 1, 15), 2000m, "USD", "Manual", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        // Set FX rate: 1 USD = 50 EGP
        await fxHandler.HandleAsync(
            new RecordFxRateCommand(null, "USD", "EGP", new DateOnly(2025, 1, 15), 50m, "Manual", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        // Act: get net worth in EGP
        var report = await netWorthHandler.HandleAsync(
            new GetNetWorthReportQuery(new DateOnly(2025, 1, 20), "EGP"),
            CancellationToken.None);

        // Assert: 10 oz * $2000 * 50 EGP/USD = 1,000,000 EGP
        var assetRow = report.Rows.Single(r => r.Category == "Asset");
        Assert.True(assetRow.IsValued);
        Assert.Equal(1_000_000m, assetRow.ReportingAmount);
        Assert.Equal(1_000_000m, report.TotalInvestmentAssets);
    }

    [Fact]
    public async Task NetWorth_IncludesCashBalances()
    {
        // Arrange
        var createAccountHandler = new CreateAccountHandler(_eventStore);
        var dashboardHandler = CreateDashboardHandler();
        var obligationsHandler = new GetObligationsListHandler(dashboardHandler);
        var netWorthHandler = new GetNetWorthReportHandler(_eventStore, obligationsHandler);

        await createAccountHandler.HandleAsync(
            new CreateAccountCommand(null, "Wallet", "Cash", 5000m, "EGP", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        await createAccountHandler.HandleAsync(
            new CreateAccountCommand(null, "Bank", "Bank", 20000m, "EGP", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Act
        var report = await netWorthHandler.HandleAsync(
            new GetNetWorthReportQuery(DateOnly.FromDateTime(DateTime.Today), "EGP"),
            CancellationToken.None);

        // Assert: 5000 + 20000 = 25000 cash
        Assert.Equal(25000m, report.TotalCash);
        Assert.Equal(2, report.Rows.Count(r => r.Category == "Cash"));
        Assert.Equal(25000m, report.KnownNetWorth);
    }

    [Fact]
    public async Task NetWorth_IncludesAssets_AndLiabilities()
    {
        // Arrange
        var createAccountHandler = new CreateAccountHandler(_eventStore);
        var createAssetHandler = new CreateAssetHandler(_eventStore);
        var priceHandler = new RecordAssetPriceHandler(_eventStore);
        var dashboardHandler = CreateDashboardHandler();
        var obligationsHandler = new GetObligationsListHandler(dashboardHandler);
        var netWorthHandler = new GetNetWorthReportHandler(_eventStore, obligationsHandler);

        // Cash: 10,000 EGP
        await createAccountHandler.HandleAsync(
            new CreateAccountCommand(null, "Cash", "Cash", 10000m, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Asset: car worth 500,000 EGP
        var carQty = JsonSerializer.Serialize(new { unit = "vehicle", amount = 1m });
        var carId = await createAssetHandler.HandleAsync(
            new CreateAssetCommand(null, "My Car", "Vehicle", "EGP",
                carQty, [], "", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        await priceHandler.HandleAsync(
            new RecordAssetPriceCommand(null, carId, new DateOnly(2025, 1, 1), 500000m, "EGP", "Manual", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        // Obligation (liability): create obligation for 200,000 EGP
        await AppendObligationCreated(Guid.NewGuid(), "Car Loan", "Loan", 200000m, "EGP", new DateOnly(2025, 1, 1));

        // Act
        var report = await netWorthHandler.HandleAsync(
            new GetNetWorthReportQuery(new DateOnly(2025, 6, 1), "EGP"),
            CancellationToken.None);

        // Assert
        Assert.Equal(10000m, report.TotalCash);
        Assert.Equal(500000m, report.TotalInvestmentAssets);
        Assert.Equal(510000m, report.TotalAssets); // cash + investments
        Assert.Equal(200000m, report.TotalLiabilities);
        Assert.Equal(310000m, report.KnownNetWorth); // 510000 - 200000

        Assert.Contains(report.Rows, r => r.Category == "Cash");
        Assert.Contains(report.Rows, r => r.Category == "Asset");
        Assert.Contains(report.Rows, r => r.Category == "Liability");
    }

    [Fact]
    public async Task MissingFxOrPrice_IsFlagged_AndTotalsExcludeUnknown()
    {
        // Arrange
        var createAssetHandler = new CreateAssetHandler(_eventStore);
        var priceHandler = new RecordAssetPriceHandler(_eventStore);
        var dashboardHandler = CreateDashboardHandler();
        var obligationsHandler = new GetObligationsListHandler(dashboardHandler);
        var netWorthHandler = new GetNetWorthReportHandler(_eventStore, obligationsHandler);

        // Asset with NO price
        var noPriceQty = JsonSerializer.Serialize(new { unit = "property", amount = 1m });
        await createAssetHandler.HandleAsync(
            new CreateAssetCommand(null, "Unpriced Land", "RealEstate", "EGP",
                noPriceQty, [], "", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Asset priced in USD with NO FX rate to EGP
        var stockQty = JsonSerializer.Serialize(new { symbol = "AAPL", units = 50m });
        var stockId = await createAssetHandler.HandleAsync(
            new CreateAssetCommand(null, "Apple Stock", "SecurityHolding", "USD",
                stockQty, [], "", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        await priceHandler.HandleAsync(
            new RecordAssetPriceCommand(null, stockId, DateOnly.FromDateTime(DateTime.Today), 200m, "USD", "Manual", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        // Act: report in EGP — no FX rate for USD?EGP
        var report = await netWorthHandler.HandleAsync(
            new GetNetWorthReportQuery(DateOnly.FromDateTime(DateTime.Today), "EGP"),
            CancellationToken.None);

        // Assert
        Assert.Equal(2, report.UnknownValueCount);
        Assert.Equal(0m, report.TotalInvestmentAssets); // both excluded
        Assert.Equal(0m, report.KnownNetWorth);

        var unpricedRow = report.Rows.Single(r => r.Name == "Unpriced Land");
        Assert.False(unpricedRow.IsValued);
        Assert.Contains("No price", unpricedRow.ValuationNote);

        var stockRow = report.Rows.Single(r => r.Name == "Apple Stock");
        Assert.False(stockRow.IsValued);
        Assert.Contains("Missing FX rate", stockRow.ValuationNote);
    }

    [Fact]
    public async Task AssetArchive_RemovesFromActiveButKeepsHistory()
    {
        // Arrange
        var createHandler = new CreateAssetHandler(_eventStore);
        var archiveHandler = new ArchiveAssetHandler(_eventStore);
        var priceHandler = new RecordAssetPriceHandler(_eventStore);
        var listHandler = new GetAssetsListHandler(_eventStore);
        var dashboardHandler = CreateDashboardHandler();
        var obligationsHandler = new GetObligationsListHandler(dashboardHandler);
        var netWorthHandler = new GetNetWorthReportHandler(_eventStore, obligationsHandler);

        var qtySpec = JsonSerializer.Serialize(new { unit = "vehicle", amount = 1m });
        var assetId = await createHandler.HandleAsync(
            new CreateAssetCommand(null, "Old Car", "Vehicle", "EGP",
                qtySpec, [], "", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        await priceHandler.HandleAsync(
            new RecordAssetPriceCommand(null, assetId, new DateOnly(2025, 1, 1), 300000m, "EGP", "Manual", ""),
            _actorUserId, _deviceId, CancellationToken.None);

        // Act: archive
        await archiveHandler.HandleAsync(
            new ArchiveAssetCommand(assetId, new DateOnly(2025, 6, 1), "Sold the car"),
            _actorUserId, _deviceId, CancellationToken.None);

        // Assert: asset is marked archived
        var assets = await listHandler.HandleAsync(ct: CancellationToken.None);
        var archived = assets.Single(a => a.AssetId == assetId);
        Assert.True(archived.IsArchived);
        Assert.Equal("Old Car", archived.Name);
        Assert.Equal(1m, archived.Quantity);

        // Net worth should NOT include archived asset
        var report = await netWorthHandler.HandleAsync(
            new GetNetWorthReportQuery(new DateOnly(2025, 7, 1), "EGP"),
            CancellationToken.None);

        Assert.DoesNotContain(report.Rows, r => r.Name == "Old Car");
        Assert.Equal(0m, report.TotalInvestmentAssets);
    }

    // --- helpers ---

    private GetPortfolioDashboardHandler CreateDashboardHandler()
    {
        return new GetPortfolioDashboardHandler(_eventStore);
    }

    private async Task AppendObligationCreated(Guid obligationId, string name, string type, decimal principal, string currencyCode, DateOnly startDate)
    {
        var currency = currencyCode switch
        {
            "EGP" => Currency.EGP,
            "USD" => Currency.USD,
            _ => new Currency(currencyCode, 2)
        };

        var ev = new ObligationCreated(obligationId, name, type, new Money(principal, currency), startDate, currencyCode);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(obligationId),
            nameof(ObligationCreated),
            DateTimeOffset.UtcNow,
            startDate,
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
