using DebtManager.Domain.Events;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Sync;
using DebtManager.Sync.Transport;

namespace DebtManager.Integration.Tests;

// Manual integration test. Run only when func + azurite are running.

public class LocalFunctionsSyncTests
{
    [Fact(Skip = "Requires local Azure Functions running on http://localhost:7071 and Azurite running.")]
    public async Task TwoDevices_Converge_ViaLocalFunctions()
    {
        // Ensure your func host is running on localhost:7071 and Azurite is running
        var baseUrl = "http://localhost:7071";
        var apiKey = "dev-key-change-me";
        var vaultId = "family1";

        var http = new HttpClient();
        var transport = new AzureSyncTransport(http, baseUrl, apiKey);

        // device A DB
        var dbA = Path.Combine(Path.GetTempPath(), $"debtmanager_fnA_{Guid.NewGuid()}.db");
        var storeA = new SqliteEventStore(new SqliteConnectionFactory(dbA, new TestKeyStore()));
        var engineA = new SyncEngine(storeA, transport);
        var deviceA = Guid.NewGuid();

        // device B DB
        var dbB = Path.Combine(Path.GetTempPath(), $"debtmanager_fnB_{Guid.NewGuid()}.db");
        var storeB = new SqliteEventStore(new SqliteConnectionFactory(dbB, new TestKeyStore()));
        var engineB = new SyncEngine(storeB, transport);
        var deviceB = Guid.NewGuid();

        // append one event on each device
        var eA = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(Guid.NewGuid()),
            "DeviceAEvent",
            DateTimeOffset.UtcNow.AddSeconds(1),
            new DateOnly(2026, 1, 1),
            Guid.NewGuid(),
            deviceA,
            Guid.NewGuid(),
            null,
            1,
            """{"a":1}"""
        );

        var eB = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(Guid.NewGuid()),
            "DeviceBEvent",
            DateTimeOffset.UtcNow.AddSeconds(2),
            new DateOnly(2026, 1, 1),
            Guid.NewGuid(),
            deviceB,
            Guid.NewGuid(),
            null,
            1,
            """{"b":1}"""
        );

        await storeA.AppendAsync(eA, CancellationToken.None);
        await storeB.AppendAsync(eB, CancellationToken.None);

        // sync cycles
        await engineA.SyncOnceAsync(vaultId, deviceA, CancellationToken.None);
        await engineB.SyncOnceAsync(vaultId, deviceB, CancellationToken.None);
        await engineA.SyncOnceAsync(vaultId, deviceA, CancellationToken.None);
        await engineB.SyncOnceAsync(vaultId, deviceB, CancellationToken.None);

        // both should contain both events
        var allA = await storeA.ReadAllAsync(DateTimeOffset.UtcNow.AddDays(-1), CancellationToken.None);
        var allB = await storeB.ReadAllAsync(DateTimeOffset.UtcNow.AddDays(-1), CancellationToken.None);

        Assert.Contains(allA, x => x.EventId.Value == eA.EventId.Value);
        Assert.Contains(allA, x => x.EventId.Value == eB.EventId.Value);

        Assert.Contains(allB, x => x.EventId.Value == eA.EventId.Value);
        Assert.Contains(allB, x => x.EventId.Value == eB.EventId.Value);

        // cleanup (same helper you used elsewhere)
        await Cleanup(dbA);
        await Cleanup(dbB);

        static async Task Cleanup(string dbPath)
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
}
