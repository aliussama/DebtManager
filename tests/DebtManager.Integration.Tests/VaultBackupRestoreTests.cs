using System.IO.Compression;
using System.Text.Json;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Events;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class VaultBackupRestoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly string _backupPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;

    public VaultBackupRestoreTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _tempDir = Path.Combine(Path.GetTempPath(), $"VaultTests_{id}");
        Directory.CreateDirectory(_tempDir);

        _dbPath = Path.Combine(_tempDir, "test.db");
        _backupPath = Path.Combine(_tempDir, "backup.dmvault");

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
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
                break;
            }
            catch (IOException) when (i < 29)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Fact]
    public async Task Backup_CreatesVaultFile_WithManifest()
    {
        // Arrange: populate DB with an event
        await AppendTestEventAsync("Test Loan");

        var fakeDialog = new FakeFileDialogService(savePath: _backupPath);
        var service = new VaultBackupService(_dbPath, _factory, fakeDialog);

        // Act
        await service.BackupAsync(CancellationToken.None);

        // Assert: file exists and is a valid ZIP
        Assert.True(File.Exists(_backupPath));

        using var zip = ZipFile.OpenRead(_backupPath);
        Assert.NotNull(zip.GetEntry("vault.db"));
        Assert.NotNull(zip.GetEntry("manifest.json"));

        // Verify manifest contents
        var manifestEntry = zip.GetEntry("manifest.json")!;
        using var sr = new StreamReader(manifestEntry.Open());
        var json = sr.ReadToEnd();
        var manifest = JsonSerializer.Deserialize<ManifestDto>(json);

        Assert.NotNull(manifest);
        Assert.True(manifest!.CreatedAtUtc > DateTime.MinValue);
        Assert.NotEmpty(manifest.AppVersion);
        Assert.NotEmpty(manifest.Files);
        Assert.Contains(manifest.Files, f => f.EntryName == "vault.db");
        Assert.All(manifest.Files, f => Assert.NotEmpty(f.Sha256));
    }

    [Fact]
    public async Task Restore_ReplacesDatabase_AndDataIsPresent()
    {
        // Arrange: create event A and backup
        await AppendTestEventAsync("Obligation A");

        var fakeDialog = new FakeFileDialogService(savePath: _backupPath);
        var service = new VaultBackupService(_dbPath, _factory, fakeDialog);
        await service.BackupAsync(CancellationToken.None);

        // Create event B (different data)
        await AppendTestEventAsync("Obligation B");

        // Verify B exists before restore
        var envsBefore = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        Assert.Contains(envsBefore, e => e.PayloadJson.Contains("Obligation B"));

        // Act: restore the backup (which only has A)
        await service.RestoreAsync(_backupPath, CancellationToken.None);

        // Assert: re-open store and check data
        var restoredFactory = new SqliteConnectionFactory(_dbPath, new TestKeyStore());
        var restoredStore = new SqliteEventStore(restoredFactory);
        var envsAfter = await restoredStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);

        Assert.Contains(envsAfter, e => e.PayloadJson.Contains("Obligation A"));
        Assert.DoesNotContain(envsAfter, e => e.PayloadJson.Contains("Obligation B"));
    }

    [Fact]
    public async Task Restore_InvalidBackup_IsRejected()
    {
        // Arrange: create a valid DB first
        await AppendTestEventAsync("Original");

        // Create a fake .dmvault missing manifest
        var fakePath = Path.Combine(_tempDir, "fake.dmvault");
        using (var zip = ZipFile.Open(fakePath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("vault.db");
            using var sw = new StreamWriter(entry.Open());
            sw.Write("not a real database");
        }

        var fakeDialog = new FakeFileDialogService(openPath: fakePath);
        var service = new VaultBackupService(_dbPath, _factory, fakeDialog);

        // Act & Assert: restore should throw (no manifest)
        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.RestoreAsync(fakePath, CancellationToken.None));

        // Original data should still be intact
        var envs = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        Assert.Contains(envs, e => e.PayloadJson.Contains("Original"));
    }

    [Fact]
    public async Task Restore_Failure_RollsBack()
    {
        // Arrange: create initial DB with data
        await AppendTestEventAsync("Keep Me");

        // Create a valid backup
        var fakeDialog = new FakeFileDialogService(savePath: _backupPath);
        var service = new VaultBackupService(_dbPath, _factory, fakeDialog);
        await service.BackupAsync(CancellationToken.None);

        // Enable fault injection for restore
        service.InjectRestoreFault = true;

        // Act & Assert: restore should fail but rollback
        await Assert.ThrowsAsync<IOException>(
            () => service.RestoreAsync(_backupPath, CancellationToken.None));

        // Original data should still be accessible
        var restoredFactory = new SqliteConnectionFactory(_dbPath, new TestKeyStore());
        var restoredStore = new SqliteEventStore(restoredFactory);
        var envs = await restoredStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);

        Assert.Contains(envs, e => e.PayloadJson.Contains("Keep Me"));
    }

    [Fact]
    public async Task Backup_UserCancels_DoesNotCreateFile()
    {
        // Dialog returns null (user cancelled)
        var fakeDialog = new FakeFileDialogService(savePath: null);
        var service = new VaultBackupService(_dbPath, _factory, fakeDialog);

        await service.BackupAsync(CancellationToken.None);

        Assert.False(File.Exists(_backupPath));
    }

    // --- helpers ---

    private async Task AppendTestEventAsync(string name)
    {
        var streamId = new StreamId(Guid.NewGuid());
        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            streamId,
            "ObligationCreated",
            DateTimeOffset.UtcNow,
            DateOnly.FromDateTime(DateTime.Today),
            Guid.NewGuid(),
            Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            CausationEventId: null,
            PayloadSchemaVersion: 1,
            PayloadJson: JsonSerializer.Serialize(new { Name = name, Type = "Loan", Amount = 10000 })
        );
        await _eventStore.AppendAsync(envelope, CancellationToken.None);
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        private readonly string? _savePath;
        private readonly string? _openPath;

        public FakeFileDialogService(string? savePath = null, string? openPath = null)
        {
            _savePath = savePath;
            _openPath = openPath;
        }

        public string? ShowSaveFileDialog(string defaultFileName, string filter, string title)
            => _savePath;

        public string? ShowOpenFileDialog(string title, string filter)
            => _openPath;
    }

    private sealed class ManifestDto
    {
        public DateTime CreatedAtUtc { get; set; }
        public string AppVersion { get; set; } = string.Empty;
        public int SchemaVersion { get; set; }
        public List<FileEntryDto> Files { get; set; } = new();
    }

    private sealed class FileEntryDto
    {
        public string EntryName { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
    }
}
