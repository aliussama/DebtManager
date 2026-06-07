using DebtManager.Application.UseCases;
using DebtManager.Domain.Services.Rules;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Rules;

namespace DebtManager.Integration.Tests;

public class PortfolioTimelineTests
{
    [Fact]
    public async Task Timeline_IncomeExpensePayment_ProducesRunningBalance()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_timeline_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(dbPath, new TestKeyStore());
        var store = new SqliteEventStore(factory);

        var actor = Guid.NewGuid();
        var device = Guid.NewGuid();

        var inc = new RecordIncomeHandler(store);
        var exp = new RecordExpenseHandler(store);

        await inc.HandleAsync(new RecordIncomeCommand(1000m, "EGP", new DateOnly(2026, 1, 1), "Salary"), actor, device, CancellationToken.None);
        await exp.HandleAsync(new RecordExpenseCommand(200m, "EGP", new DateOnly(2026, 1, 2), "Bills", "Internet"), actor, device, CancellationToken.None);

        var repo = new SqliteRulePackRepository(factory);
        var resolver = new SqliteRulePackResolver(store);
        var ruleEngine = new SqliteRuleEngine(repo, resolver);

        // PaymentMade exists in your system, so record one through your existing handler:
        var pay = new RecordPaymentHandler(store, ruleEngine);
        await pay.HandleAsync(
            new RecordPaymentCommand(
                ObligationId: Guid.NewGuid(),
                Amount: 300m,
                CurrencyCode: "EGP",
                EffectiveDate: new DateOnly(2026, 1, 3),
                Reference: "TestPay"
            ),
            actor, device, CancellationToken.None);

        var snapshots = new GetFinancialSnapshotHandler(store, ruleEngine);
        var handler = new GetPortfolioTimelineHandler(store, snapshots);
        var result = await handler.HandleAsync(new DateOnly(2026, 1, 5), CancellationToken.None);

        Assert.Equal(3, result.Items.Count);

        Assert.Equal(1000m, result.Items[0].RunningBalance.Amount);
        Assert.Equal(800m, result.Items[1].RunningBalance.Amount);
        Assert.Equal(500m, result.Items[2].RunningBalance.Amount);

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
