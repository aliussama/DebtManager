using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Tax;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class TaxYearReportingTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public TaxYearReportingTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"TaxYearReportingTests_{id}.db");
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
    public async Task TaxProfile_CreateModifyArchive_Works()
    {
        var createHandler = new CreateTaxProfileHandler(_eventStore);
        var modifyHandler = new ModifyTaxProfileHandler(_eventStore);
        var archiveHandler = new ArchiveTaxProfileHandler(_eventStore);
        var getHandler = new GetTaxProfilesHandler(_eventStore);

        // Create
        var profileId = await createHandler.HandleAsync(
            new CreateTaxProfileCommand(null, "US Tax", "US", 1, 1, "USD",
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var profiles = await getHandler.HandleAsync(CancellationToken.None);
        Assert.Single(profiles);
        Assert.Equal("US Tax", profiles[0].Name);
        Assert.Equal("US", profiles[0].CountryCode);
        Assert.Equal(1, profiles[0].TaxYearStartMonth);
        Assert.Equal("USD", profiles[0].BaseCurrencyCode);
        Assert.False(profiles[0].IsArchived);

        // Modify
        await modifyHandler.HandleAsync(
            new ModifyTaxProfileCommand(profileId, "US Tax 2025", null, null, null, null,
                new DateOnly(2025, 1, 2)),
            _actorUserId, _deviceId, CancellationToken.None);

        profiles = await getHandler.HandleAsync(CancellationToken.None);
        Assert.Equal("US Tax 2025", profiles[0].Name);
        Assert.Equal("US", profiles[0].CountryCode); // unchanged

        // Archive
        await archiveHandler.HandleAsync(
            new ArchiveTaxProfileCommand(profileId, new DateOnly(2025, 6, 1), "No longer needed"),
            _actorUserId, _deviceId, CancellationToken.None);

        profiles = await getHandler.HandleAsync(CancellationToken.None);
        Assert.True(profiles[0].IsArchived);
    }

    [Fact]
    public async Task TaxRule_Mapping_AppliesToCashExpenseCategory()
    {
        var profileId = await CreateTaxProfile("EG Tax", "EG", 1, 1, "EGP");
        var ruleHandler = new DefineTaxRuleHandler(_eventStore);
        var reportHandler = new GetTaxYearReportHandler(_eventStore);

        // Define rule: Rent -> DeductibleExpense
        await ruleHandler.HandleAsync(
            new DefineTaxRuleCommand(null, "ExpenseCategory", "Rent", TaxCategories.DeductibleExpense,
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Create cash account + expense
        await CreateAccount(Guid.NewGuid(), "Checking", "EGP", new DateOnly(2025, 1, 1));
        var accountId = (await new GetAccountsListHandler(_eventStore).HandleAsync(CancellationToken.None))[0].AccountId;

        await RecordExpense(accountId, 5000m, "EGP", new DateOnly(2025, 3, 15), "Rent", "March rent");

        // Record FX for EGP->EGP (same currency, rate = 1)
        var report = await reportHandler.HandleAsync(profileId, 2025, CancellationToken.None);

        Assert.Single(report.Deductions);
        Assert.Equal(5000m, report.Deductions[0].Amount);
        Assert.Equal("Rent", report.Deductions[0].Category);
        Assert.Equal(5000m, report.TotalDeductions);
        Assert.Equal(0, report.UnclassifiedCount);
    }

    [Fact]
    public async Task CapitalGains_Report_FromFifoSell_CorrectAmounts()
    {
        var profileId = await CreateTaxProfile("US Tax", "US", 1, 1, "USD");
        var reportHandler = new GetTaxYearReportHandler(_eventStore);

        var accountId = await CreateInvestmentAccount("Brokerage", "USD");
        var assetId = await CreateAsset("AAPL", "USD");

        // Buy 10 at $100 on Jan 10
        await RecordInvestmentTransaction(accountId, assetId, "AAPL", "Buy",
            new DateOnly(2025, 1, 10), 10m, 100m, 0m, 0m, "USD");

        // Buy 10 at $120 on Feb 10
        await RecordInvestmentTransaction(accountId, assetId, "AAPL", "Buy",
            new DateOnly(2025, 2, 10), 10m, 120m, 0m, 0m, "USD");

        // Sell 15 at $130 on Mar 10 (FIFO: 10 from $100 lot, 5 from $120 lot)
        await RecordInvestmentTransaction(accountId, assetId, "AAPL", "Sell",
            new DateOnly(2025, 3, 10), 15m, 130m, 0m, 0m, "USD");

        var report = await reportHandler.HandleAsync(profileId, 2025, CancellationToken.None);

        Assert.Single(report.CapitalGains);
        var cg = report.CapitalGains[0];
        Assert.Equal("AAPL", cg.Symbol);
        Assert.Equal(15m, cg.QuantitySold);
        // Proceeds = 15 * 130 = 1950
        Assert.Equal(1950m, cg.Proceeds);
        // Cost basis FIFO = 10*100 + 5*120 = 1600
        Assert.Equal(1600m, cg.CostBasis);
        // Gain = 1950 - 1600 = 350
        Assert.Equal(350m, cg.RealizedGain);
        Assert.Equal(350m, report.TotalCapitalGains);
    }

    [Fact]
    public async Task CapitalGains_Report_FromAverageCostSell_CorrectAmounts()
    {
        var profileId = await CreateTaxProfile("US Tax", "US", 1, 1, "USD");
        var reportHandler = new GetTaxYearReportHandler(_eventStore);
        var modeHandler = new SetCostBasisModeHandler(_eventStore);

        var accountId = await CreateInvestmentAccount("AvgCost Brokerage", "USD");

        // Set average cost mode
        await modeHandler.HandleAsync(
            new SetCostBasisModeCommand(accountId, "AverageCost", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var assetId = await CreateAsset("MSFT", "USD");

        // Buy 10 at $200 (cost = 2000)
        await RecordInvestmentTransaction(accountId, assetId, "MSFT", "Buy",
            new DateOnly(2025, 1, 10), 10m, 200m, 0m, 0m, "USD");

        // Buy 10 at $300 (cost = 3000) => avg = 250
        await RecordInvestmentTransaction(accountId, assetId, "MSFT", "Buy",
            new DateOnly(2025, 2, 10), 10m, 300m, 0m, 0m, "USD");

        // Sell 5 at $350 => cost basis = 5 * 250 = 1250, proceeds = 1750, gain = 500
        await RecordInvestmentTransaction(accountId, assetId, "MSFT", "Sell",
            new DateOnly(2025, 3, 10), 5m, 350m, 0m, 0m, "USD");

        var report = await reportHandler.HandleAsync(profileId, 2025, CancellationToken.None);

        Assert.Single(report.CapitalGains);
        var cg = report.CapitalGains[0];
        Assert.Equal(1750m, cg.Proceeds);
        Assert.Equal(1250m, cg.CostBasis);
        Assert.Equal(500m, cg.RealizedGain);
        Assert.Equal(500m, report.TotalCapitalGains);
    }

    [Fact]
    public async Task DividendAndInterest_AppearInIncomeSection()
    {
        var profileId = await CreateTaxProfile("US Tax", "US", 1, 1, "USD");
        var reportHandler = new GetTaxYearReportHandler(_eventStore);

        var accountId = await CreateInvestmentAccount("Brokerage", "USD");
        var assetId = await CreateAsset("VTI", "USD");

        // Dividend: 100 shares * $0.50 = $50 net
        await RecordInvestmentTransaction(accountId, assetId, "VTI", "Dividend",
            new DateOnly(2025, 6, 15), 100m, 0.50m, 0m, 0m, "USD");

        // Interest: 1000 * $0.02 = $20 net
        await RecordInvestmentTransaction(accountId, assetId, "VTI", "Interest",
            new DateOnly(2025, 9, 15), 1000m, 0.02m, 0m, 0m, "USD");

        var report = await reportHandler.HandleAsync(profileId, 2025, CancellationToken.None);

        Assert.Equal(2, report.IncomeLines.Count);
        var dividend = report.IncomeLines.First(i => i.IncomeType == TaxCategories.DividendIncome);
        var interest = report.IncomeLines.First(i => i.IncomeType == TaxCategories.InterestIncome);

        Assert.Equal(50m, dividend.Amount);
        Assert.Equal(20m, interest.Amount);
        Assert.Equal(50m, report.TotalDividendIncome);
        Assert.Equal(20m, report.TotalInterestIncome);
    }

    [Fact]
    public async Task Unclassified_Items_Appear_WhenNoRulesOrConfirmation()
    {
        var profileId = await CreateTaxProfile("EG Tax", "EG", 1, 1, "EGP");
        var reportHandler = new GetTaxYearReportHandler(_eventStore);

        // Create cash account + expense with no matching rule
        await CreateAccount(Guid.NewGuid(), "Checking", "EGP", new DateOnly(2025, 1, 1));
        var accountId = (await new GetAccountsListHandler(_eventStore).HandleAsync(CancellationToken.None))[0].AccountId;

        await RecordExpense(accountId, 300m, "EGP", new DateOnly(2025, 4, 1), "Misc", "Random purchase");

        var report = await reportHandler.HandleAsync(profileId, 2025, CancellationToken.None);

        // Expense with no rule => Unclassified
        Assert.Equal(1, report.UnclassifiedCount);
        Assert.Single(report.UnclassifiedItems);
        Assert.Equal("CashLedger", report.UnclassifiedItems[0].SourceType);
    }

    [Fact]
    public async Task MissingFxOrPrice_FlagsUnknown_AndTotalsExcludeUnknown()
    {
        var profileId = await CreateTaxProfile("US Tax", "US", 1, 1, "USD");
        var reportHandler = new GetTaxYearReportHandler(_eventStore);

        var accountId = await CreateInvestmentAccount("EUR Brokerage", "EUR");
        var assetId = await CreateAsset("BMW.DE", "EUR");

        // Buy in EUR
        await RecordInvestmentTransaction(accountId, assetId, "BMW.DE", "Buy",
            new DateOnly(2025, 1, 10), 10m, 100m, 0m, 0m, "EUR");

        // Sell in EUR at higher price => Gain in EUR but no FX rate recorded
        await RecordInvestmentTransaction(accountId, assetId, "BMW.DE", "Sell",
            new DateOnly(2025, 6, 10), 10m, 120m, 0m, 0m, "EUR");

        // Dividend in EUR
        await RecordInvestmentTransaction(accountId, assetId, "BMW.DE", "Dividend",
            new DateOnly(2025, 7, 1), 10m, 1m, 0m, 0m, "EUR");

        var report = await reportHandler.HandleAsync(profileId, 2025, CancellationToken.None);

        // Capital gains exist but not valued (no FX EUR->USD)
        Assert.Single(report.CapitalGains);
        Assert.False(report.CapitalGains[0].IsValued);
        Assert.Null(report.CapitalGains[0].AmountInBaseCurrency);

        // Total capital gains = 0 because excluded
        Assert.Equal(0m, report.TotalCapitalGains);

        // Unknown count reflects items with no FX
        Assert.True(report.UnknownValueCount >= 1);
    }

    [Fact]
    public async Task Export_Csv_HasAllSectionsAndStableHeaders()
    {
        var profileId = await CreateTaxProfile("US Tax", "US", 1, 1, "USD");
        var reportHandler = new GetTaxYearReportHandler(_eventStore);

        var accountId = await CreateInvestmentAccount("Brokerage", "USD");
        var assetId = await CreateAsset("AAPL", "USD");

        await RecordInvestmentTransaction(accountId, assetId, "AAPL", "Buy",
            new DateOnly(2025, 1, 10), 10m, 100m, 0m, 0m, "USD");
        await RecordInvestmentTransaction(accountId, assetId, "AAPL", "Sell",
            new DateOnly(2025, 3, 10), 5m, 120m, 0m, 0m, "USD");
        await RecordInvestmentTransaction(accountId, assetId, "AAPL", "Dividend",
            new DateOnly(2025, 6, 15), 10m, 0.50m, 0m, 0m, "USD");

        // Add a cash expense (unclassified)
        await CreateAccount(Guid.NewGuid(), "Checking", "USD", new DateOnly(2025, 1, 1));
        var cashAcctId = (await new GetAccountsListHandler(_eventStore).HandleAsync(CancellationToken.None))[0].AccountId;
        await RecordExpense(cashAcctId, 100m, "USD", new DateOnly(2025, 5, 1), "Utilities", "Electric bill");

        var report = await reportHandler.HandleAsync(profileId, 2025, CancellationToken.None);

        using var sw = new StringWriter();
        GetTaxYearReportHandler.WriteCsvReport(report, sw);
        var csv = sw.ToString();

        // Verify all 5 sections exist
        Assert.Contains("[SUMMARY]", csv);
        Assert.Contains("[CAPITAL_GAINS]", csv);
        Assert.Contains("[INCOME]", csv);
        Assert.Contains("[DEDUCTIONS]", csv);
        Assert.Contains("[UNCLASSIFIED]", csv);

        // Verify stable headers
        Assert.Contains("Field,Value", csv);
        Assert.Contains("SellTransactionId,Symbol,TradeDate,QuantitySold,Proceeds,CostBasis,Fees,Taxes,RealizedGain,HoldingPeriodDays,Currency,AmountInBase,IsValued,Note", csv);
        Assert.Contains("SourceId,SourceType,Symbol,IncomeType,Date,Amount,Currency,AmountInBase,IsValued,Note", csv);
        Assert.Contains("SourceId,Category,Date,Amount,Currency,AmountInBase,IsValued,Note", csv);
        Assert.Contains("SourceId,SourceType,Description,Date,Amount,Currency", csv);

        // Verify summary values
        Assert.Contains("ProfileName,US Tax", csv);
        Assert.Contains("TaxYear,2025", csv);
        Assert.Contains("BaseCurrency,USD", csv);
    }

    // --- Helpers ---

    private async Task<Guid> CreateTaxProfile(string name, string country, int startMonth, int startDay, string baseCcy)
    {
        var handler = new CreateTaxProfileHandler(_eventStore);
        return await handler.HandleAsync(
            new CreateTaxProfileCommand(null, name, country, startMonth, startDay, baseCcy,
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> CreateInvestmentAccount(string name, string ccy)
    {
        var handler = new CreateInvestmentAccountHandler(_eventStore);
        return await handler.HandleAsync(
            new CreateInvestmentAccountCommand(null, name, ccy, "Broker", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

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

    private async Task RecordInvestmentTransaction(Guid accountId, Guid assetId, string symbol,
        string type, DateOnly tradeDate, decimal qty, decimal price, decimal fees, decimal taxes, string ccy)
    {
        var handler = new RecordInvestmentTransactionHandler(_eventStore);
        await handler.HandleAsync(
            new RecordInvestmentTransactionCommand(null, accountId, assetId, symbol,
                type, tradeDate, null, qty, price, fees, taxes, ccy, null, "", ""),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task CreateAccount(Guid accountId, string name, string ccy, DateOnly date)
    {
        var ev = new AccountCreated(accountId, name, "Cash", ccy, 0m, date);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(accountId),
            nameof(AccountCreated), DateTimeOffset.UtcNow, date,
            _actorUserId, _deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);
    }

    private async Task RecordExpense(Guid accountId, decimal amount, string ccy, DateOnly date, string category, string notes)
    {
        var money = new Money(amount, new Currency(ccy, 2));
        var ev = new ExpenseRecorded(accountId, money, date, category, notes);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(accountId),
            nameof(ExpenseRecorded), DateTimeOffset.UtcNow, date,
            _actorUserId, _deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);
    }
}
