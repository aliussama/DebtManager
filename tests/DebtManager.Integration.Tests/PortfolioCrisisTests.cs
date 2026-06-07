using DebtManager.Application.UseCases;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public class PortfolioCrisisTests
{
    [Fact]
    public async Task CrisisDetected_WhenExpensesExceedIncome()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_crisis_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(dbPath, new TestKeyStore());
        var store = new SqliteEventStore(factory);

        var actor = Guid.NewGuid();
        var device = Guid.NewGuid();

        // Income 1000 on Jan 1
        var inc = new RecordIncomeHandler(store);
        await inc.HandleAsync(new RecordIncomeCommand(1000m, "EGP", new DateOnly(2026, 1, 1), "Salary"), actor, device, CancellationToken.None);

        // Expense 1500 on Jan 2 -> crisis starts
        var exp = new RecordExpenseHandler(store);
        await exp.HandleAsync(new RecordExpenseCommand(1500m, "EGP", new DateOnly(2026, 1, 2), "Emergency", "Repair"), actor, device, CancellationToken.None);

        // Build timeline + crisis detector (No charges needed here)
        var ruleEngine = new DebtManager.Domain.Services.Rules.NoOpRuleEngine();
        var snapshots = new GetFinancialSnapshotHandler(store, ruleEngine);
        var timeline = new GetPortfolioTimelineHandler(store, snapshots);

        var crisis = new DetectPortfolioCrisisHandler(timeline);
        var windows = await crisis.HandleAsync(new DateOnly(2026, 1, 5), CancellationToken.None);
        Assert.True(windows.Count >= 1);

        var w = windows[0];
        Assert.True(w.LowestBalance.Amount < 0m);
        Assert.True(w.TopContributors.Count > 0);

        Assert.Equal(new DateOnly(2026, 1, 2), w.Start);
        Assert.Contains(w.TopContributors, c => c.Type == "Expense");

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
