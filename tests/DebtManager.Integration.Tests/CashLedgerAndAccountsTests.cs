using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class CashLedgerAndAccountsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public CashLedgerAndAccountsTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"CashLedgerTests_{id}.db");
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
    public async Task CreateAccount_ShowsInList_WithOpeningBalance()
    {
        // Arrange
        var createHandler = new CreateAccountHandler(_eventStore);
        var listHandler = new GetAccountsListHandler(_eventStore);

        // Act
        var accountId = await createHandler.HandleAsync(
            new CreateAccountCommand(null, "My Wallet", "Cash", 500m, "EGP", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var accounts = await listHandler.HandleAsync(CancellationToken.None);

        // Assert
        Assert.Single(accounts);
        var account = accounts[0];
        Assert.Equal("My Wallet", account.Name);
        Assert.Equal("Cash", account.AccountType);
        Assert.Equal("EGP", account.CurrencyCode);
        Assert.Equal(500m, account.Balance);
        Assert.False(account.IsArchived);
    }

    [Fact]
    public async Task RecordIncome_IncreasesBalance()
    {
        // Arrange
        var createHandler = new CreateAccountHandler(_eventStore);
        var incomeHandler = new RecordIncomeHandler(_eventStore);
        var listHandler = new GetAccountsListHandler(_eventStore);

        var accountId = await createHandler.HandleAsync(
            new CreateAccountCommand(null, "Savings", "Bank", 1000m, "EGP", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Act: record income into this account
        await AppendIncomeEvent(accountId, 2500m, "EGP", "Salary");

        var accounts = await listHandler.HandleAsync(CancellationToken.None);

        // Assert
        var account = accounts.Single(a => a.AccountId == accountId);
        Assert.Equal(3500m, account.Balance); // 1000 opening + 2500 income
    }

    [Fact]
    public async Task RecordExpense_DecreasesBalance_AndCategoryTracked()
    {
        // Arrange
        var createHandler = new CreateAccountHandler(_eventStore);
        var listHandler = new GetAccountsListHandler(_eventStore);
        var ledgerHandler = new GetCashLedgerHandler(_eventStore);

        var accountId = await createHandler.HandleAsync(
            new CreateAccountCommand(null, "Daily", "Cash", 5000m, "EGP", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Act: record expense
        await AppendExpenseEvent(accountId, 150m, "EGP", "Food", "Lunch");

        var accounts = await listHandler.HandleAsync(CancellationToken.None);
        var ledger = await ledgerHandler.HandleAsync(new CashLedgerQuery(), CancellationToken.None);

        // Assert
        var account = accounts.Single(a => a.AccountId == accountId);
        Assert.Equal(4850m, account.Balance); // 5000 - 150

        var expenseRow = ledger.Rows.FirstOrDefault(r => r.Category == "Food");
        Assert.NotNull(expenseRow);
        Assert.Equal("Out", expenseRow.Direction);
        Assert.Equal(150m, expenseRow.Amount);
    }

    [Fact]
    public async Task Transfer_MovesMoneyBetweenAccounts_NetZeroPortfolio()
    {
        // Arrange
        var createHandler = new CreateAccountHandler(_eventStore);
        var transferHandler = new RecordTransferHandler(_eventStore);
        var listHandler = new GetAccountsListHandler(_eventStore);

        var walletId = await createHandler.HandleAsync(
            new CreateAccountCommand(null, "Wallet", "Cash", 3000m, "EGP", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        var bankId = await createHandler.HandleAsync(
            new CreateAccountCommand(null, "Bank", "Bank", 10000m, "EGP", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Act: transfer 2000 from Bank to Wallet
        await transferHandler.HandleAsync(
            new RecordTransferCommand(null, bankId, walletId, 2000m, "EGP", DateOnly.FromDateTime(DateTime.Today), "ATM withdrawal"),
            _actorUserId, _deviceId, CancellationToken.None);

        var accounts = await listHandler.HandleAsync(CancellationToken.None);

        // Assert
        var wallet = accounts.Single(a => a.AccountId == walletId);
        var bank = accounts.Single(a => a.AccountId == bankId);

        Assert.Equal(5000m, wallet.Balance);  // 3000 + 2000
        Assert.Equal(8000m, bank.Balance);    // 10000 - 2000

        // Net portfolio unchanged: 5000 + 8000 = 13000 = 3000 + 10000
        Assert.Equal(13000m, wallet.Balance + bank.Balance);
    }

    [Fact]
    public async Task GetCashLedger_FiltersByAccountAndDateRange()
    {
        // Arrange
        var createHandler = new CreateAccountHandler(_eventStore);

        var accountAId = await createHandler.HandleAsync(
            new CreateAccountCommand(null, "Account A", "Cash", 1000m, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var accountBId = await createHandler.HandleAsync(
            new CreateAccountCommand(null, "Account B", "Cash", 2000m, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        await AppendIncomeEvent(accountAId, 500m, "EGP", "Jan Income", new DateOnly(2025, 1, 15));
        await AppendIncomeEvent(accountBId, 800m, "EGP", "Feb Income", new DateOnly(2025, 2, 15));

        var ledgerHandler = new GetCashLedgerHandler(_eventStore);

        // Act: filter by Account A only
        var resultA = await ledgerHandler.HandleAsync(
            new CashLedgerQuery(AccountId: accountAId), CancellationToken.None);

        // Assert: only Account A rows
        Assert.All(resultA.Rows, r => Assert.Equal(accountAId, r.AccountId));
        Assert.Contains(resultA.Rows, r => r.Reference == "Jan Income");

        // Act: filter by date range (Feb only)
        var resultFeb = await ledgerHandler.HandleAsync(
            new CashLedgerQuery(FromDate: new DateOnly(2025, 2, 1), ToDate: new DateOnly(2025, 2, 28)),
            CancellationToken.None);

        Assert.Contains(resultFeb.Rows, r => r.Reference == "Feb Income");
        Assert.DoesNotContain(resultFeb.Rows, r => r.Reference == "Jan Income");
    }

    [Fact]
    public async Task ArchiveAccount_MarksArchived_ButLedgerStillShowsHistory()
    {
        // Arrange
        var createHandler = new CreateAccountHandler(_eventStore);
        var archiveHandler = new ArchiveAccountHandler(_eventStore);
        var listHandler = new GetAccountsListHandler(_eventStore);
        var ledgerHandler = new GetCashLedgerHandler(_eventStore);

        var accountId = await createHandler.HandleAsync(
            new CreateAccountCommand(null, "Old Account", "Cash", 1000m, "EGP", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        await AppendIncomeEvent(accountId, 200m, "EGP", "Some Income");

        // Act: archive the account
        await archiveHandler.HandleAsync(
            new ArchiveAccountCommand(accountId, DateOnly.FromDateTime(DateTime.Today), "No longer needed"),
            _actorUserId, _deviceId, CancellationToken.None);

        var accounts = await listHandler.HandleAsync(CancellationToken.None);
        var ledger = await ledgerHandler.HandleAsync(new CashLedgerQuery(), CancellationToken.None);

        // Assert: account is marked archived
        var account = accounts.Single(a => a.AccountId == accountId);
        Assert.True(account.IsArchived);

        // Balance still computed correctly
        Assert.Equal(1200m, account.Balance);

        // Ledger still shows history
        Assert.Contains(ledger.Rows, r => r.AccountId == accountId && r.Reference == "Some Income");
    }

    // --- helpers ---

    private async Task AppendIncomeEvent(Guid accountId, decimal amount, string currency, string source, DateOnly? date = null)
    {
        var effectiveDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var currencyObj = currency switch
        {
            "EGP" => Currency.EGP,
            "USD" => Currency.USD,
            _ => new Currency(currency, 2)
        };

        var ev = new IncomeRecorded(accountId, new Money(amount, currencyObj), effectiveDate, source);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(accountId),
            nameof(IncomeRecorded),
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
