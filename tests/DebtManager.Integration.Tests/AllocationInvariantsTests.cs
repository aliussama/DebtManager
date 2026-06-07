using DebtManager.Application.UseCases;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Rules;
using Xunit;

namespace DebtManager.Integration.Tests;

public class AllocationInvariantsTests
{
    [Fact]
    public async Task Overpayment_IsRecorded_AsUnapplied()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_alloc_inv_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(dbPath, new TestKeyStore());
        var store = new SqliteEventStore(factory);

        var repo = new SqliteRulePackRepository(factory);
        var resolver = new SqliteRulePackResolver(store);
        var engine = new SqliteRuleEngine(repo, resolver);

        var actor = Guid.NewGuid();
        var device = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        // obligation
        var create = new CreateObligationHandler(store);
        await create.HandleAsync(new CreateObligationCommand(
            ObligationId: obligationId,
            Name: "Alloc Inv",
            ObligationType: "Loan",
            PrincipalAmount: 1000,
            CurrencyCode: "EGP",
            StartDate: new DateOnly(2026, 1, 1)
        ), actor, device, CancellationToken.None);

        // pay more than principal (no schedule needed to test unapplied)
        var record = new RecordPaymentHandler(store, engine);
        await record.HandleAsync(new RecordPaymentCommand(
            ObligationId: obligationId,
            Amount: 1500,
            CurrencyCode: "EGP",
            EffectiveDate: new DateOnly(2026, 1, 10),
            Reference: "overpay"
        ), actor, device, CancellationToken.None);

        var snapshot = new GetFinancialSnapshotHandler(store, engine);
        var state = await snapshot.HandleAsync(obligationId, new DateOnly(2026, 1, 10), CancellationToken.None);

        Assert.True(state.UnappliedPayments.Amount > 0m);

        await Cleanup(dbPath);
    }

    private static async Task Cleanup(string dbPath)
    {
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
