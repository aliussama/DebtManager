using DebtManager.Application.UseCases;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public class PortfolioCashTests
{
    [Fact]
    public async Task IncomeMinusExpense_ProducesCorrectBalance()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_cash_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(dbPath, new TestKeyStore());
        var store = new SqliteEventStore(factory);

        var actor = Guid.NewGuid();
        var device = Guid.NewGuid();

        var inc = new RecordIncomeHandler(store);
        var exp = new RecordExpenseHandler(store);

        await inc.HandleAsync(new RecordIncomeCommand(1000m, "EGP", new DateOnly(2026, 1, 1), "Salary"), actor, device, CancellationToken.None);
        await exp.HandleAsync(new RecordExpenseCommand(200m, "EGP", new DateOnly(2026, 1, 2), "Bills", "Internet"), actor, device, CancellationToken.None);

        var snap = new GetPortfolioCashSnapshotHandler(store);
        var result = await snap.HandleAsync(new DateOnly(2026, 1, 3), CancellationToken.None);

        Assert.Equal(800m, result.Balance.Amount);

        // cleanup
        var wal = dbPath + "-wal";
        var shm = dbPath + "-shm";
        for (int i = 0; i < 30; i++)
        {
            try
            {
                if (File.Exists(wal)) File.Delete(wal);
                if (File.Exists(shm)) File.Delete(shm);
                if (File.Exists(dbPath)) File.Delete(dbPath);
                break;
            }
            catch (IOException) when (i < 29)
            {
                await Task.Delay(100);
            }
        }
    }
}
