using System.Security.Cryptography;
using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Documents;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Documents;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class DocumentVaultTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _blobDir;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly EncryptedFileDocumentBlobStore _blobStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public DocumentVaultTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"DocVaultTests_{id}.db");
        _blobDir = Path.Combine(Path.GetTempPath(), $"DocVaultBlobs_{id}");
        Directory.CreateDirectory(_blobDir);
        _factory = new SqliteConnectionFactory(_dbPath, new TestKeyStore());
        _eventStore = new SqliteEventStore(_factory);
        _blobStore = new EncryptedFileDocumentBlobStore(new TestKeyStore(), _blobDir);
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
                if (Directory.Exists(_blobDir)) Directory.Delete(_blobDir, true);
                break;
            }
            catch (IOException) when (i < 29) { Thread.Sleep(100); }
        }
    }

    // ----------------------------------------------------------------
    // 1) AddDocument_WritesCreatedEvent_AndBlobSavedEncrypted
    // ----------------------------------------------------------------
    [Fact]
    public async Task AddDocument_WritesCreatedEvent_AndBlobSavedEncrypted()
    {
        var handler = new AddDocumentHandler(_eventStore, _blobStore);
        var bytes = "Hello, encrypted world!"u8.ToArray();

        var docId = await handler.HandleAsync(
            new AddDocumentCommand(null, "receipt.pdf", "application/pdf", "[]", "Test doc",
                new DateOnly(2025, 1, 1), bytes),
            _actorUserId, _deviceId, CancellationToken.None);

        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        Assert.Contains(envelopes, e => e.EventType == nameof(DocumentCreated));

        var state = DocumentVaultProjector.Project(envelopes);
        Assert.True(state.Documents.ContainsKey(docId));
        var doc = state.Documents[docId];

        // Blob file exists and is encrypted (not plaintext)
        var blobPath = Path.Combine(_blobDir, doc.StorageKey + ".bin");
        Assert.True(File.Exists(blobPath));
        var rawBytes = await File.ReadAllBytesAsync(blobPath);
        Assert.NotEqual(bytes, rawBytes);

        // Can decrypt and get back original
        var decrypted = await _blobStore.LoadAsync(doc.StorageKey, CancellationToken.None);
        Assert.Equal(bytes, decrypted);
    }

    // ----------------------------------------------------------------
    // 2) GetDocumentsList_ReturnsAddedDoc
    // ----------------------------------------------------------------
    [Fact]
    public async Task GetDocumentsList_ReturnsAddedDoc()
    {
        var addHandler = new AddDocumentHandler(_eventStore, _blobStore);
        var listHandler = new GetDocumentsListHandler(_eventStore, _blobStore);
        var bytes = new byte[] { 1, 2, 3, 4 };

        await addHandler.HandleAsync(
            new AddDocumentCommand(null, "invoice.pdf", null, "[\"finance\"]", "My invoice",
                new DateOnly(2025, 2, 1), bytes),
            _actorUserId, _deviceId, CancellationToken.None);

        var list = await listHandler.HandleAsync(null, null, null, null, null, CancellationToken.None);
        Assert.Single(list);
        Assert.Equal("invoice.pdf", list[0].FileName);
        Assert.True(list[0].BlobExists);
    }

    // ----------------------------------------------------------------
    // 3) LinkAndUnlink_WritesEvents_AndProjectionIndexesCorrect
    // ----------------------------------------------------------------
    [Fact]
    public async Task LinkAndUnlink_WritesEvents_AndProjectionIndexesCorrect()
    {
        var addHandler = new AddDocumentHandler(_eventStore, _blobStore);
        var linkHandler = new LinkDocumentHandler(_eventStore);
        var unlinkHandler = new UnlinkDocumentHandler(_eventStore);
        var forEntityHandler = new GetDocumentsForEntityHandler(_eventStore, _blobStore);
        var bytes = new byte[] { 10, 20, 30 };

        var docId = await addHandler.HandleAsync(
            new AddDocumentCommand(null, "contract.pdf", null, "[]", "",
                new DateOnly(2025, 3, 1), bytes),
            _actorUserId, _deviceId, CancellationToken.None);

        var billId = Guid.NewGuid().ToString();
        await linkHandler.HandleAsync(
            new LinkDocumentCommand(docId, "Bill", billId, "Receipt", new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var linked = await forEntityHandler.HandleAsync("Bill", billId, CancellationToken.None);
        Assert.Single(linked);
        Assert.Equal("contract.pdf", linked[0].FileName);

        // Unlink
        await unlinkHandler.HandleAsync(
            new UnlinkDocumentCommand(docId, "Bill", billId, "No longer needed", new DateOnly(2025, 3, 2)),
            _actorUserId, _deviceId, CancellationToken.None);

        linked = await forEntityHandler.HandleAsync("Bill", billId, CancellationToken.None);
        Assert.Empty(linked);

        // Events exist
        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        Assert.Contains(envelopes, e => e.EventType == nameof(DocumentLinked));
        Assert.Contains(envelopes, e => e.EventType == nameof(DocumentUnlinked));
    }

    // ----------------------------------------------------------------
    // 4) PurgeBlob_RemovesLocalBlob_ButKeepsMetadata
    // ----------------------------------------------------------------
    [Fact]
    public async Task PurgeBlob_RemovesLocalBlob_ButKeepsMetadata()
    {
        var addHandler = new AddDocumentHandler(_eventStore, _blobStore);
        var purgeHandler = new PurgeDocumentBlobHandler(_eventStore, _blobStore);
        var bytes = new byte[] { 99, 88, 77 };

        var docId = await addHandler.HandleAsync(
            new AddDocumentCommand(null, "statement.pdf", null, "[]", "",
                new DateOnly(2025, 4, 1), bytes),
            _actorUserId, _deviceId, CancellationToken.None);

        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state = DocumentVaultProjector.Project(envelopes);
        var storageKey = state.Documents[docId].StorageKey;
        Assert.True(await _blobStore.ExistsAsync(storageKey, CancellationToken.None));

        // Purge
        await purgeHandler.HandleAsync(
            new PurgeDocumentBlobCommand(docId, "Free space", new DateOnly(2025, 4, 2)),
            _actorUserId, _deviceId, CancellationToken.None);

        Assert.False(await _blobStore.ExistsAsync(storageKey, CancellationToken.None));

        // Metadata still exists
        envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        state = DocumentVaultProjector.Project(envelopes);
        Assert.True(state.Documents.ContainsKey(docId));
        Assert.True(state.Documents[docId].IsBlobPurged);
        Assert.Contains(envelopes, e => e.EventType == nameof(DocumentBlobPurged));
    }

    // ----------------------------------------------------------------
    // 5) ExportDocument_WritesExportedEvent_AndExportsCorrectBytes
    // ----------------------------------------------------------------
    [Fact]
    public async Task ExportDocument_WritesExportedEvent_AndExportsCorrectBytes()
    {
        var addHandler = new AddDocumentHandler(_eventStore, _blobStore);
        var exportHandler = new ExportDocumentHandler(_eventStore, _blobStore);
        var originalBytes = "Export me!"u8.ToArray();

        var docId = await addHandler.HandleAsync(
            new AddDocumentCommand(null, "export_test.txt", "text/plain", "[]", "",
                new DateOnly(2025, 5, 1), originalBytes),
            _actorUserId, _deviceId, CancellationToken.None);

        var exportPath = Path.Combine(Path.GetTempPath(), $"exported_{Guid.NewGuid():N}.txt");
        try
        {
            await exportHandler.HandleAsync(
                new ExportDocumentCommand(docId, exportPath, new DateOnly(2025, 5, 2)),
                _actorUserId, _deviceId, CancellationToken.None);

            Assert.True(File.Exists(exportPath));
            var exported = await File.ReadAllBytesAsync(exportPath);
            Assert.Equal(originalBytes, exported);

            var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
            Assert.Contains(envelopes, e => e.EventType == nameof(DocumentExported));
        }
        finally
        {
            if (File.Exists(exportPath)) File.Delete(exportPath);
        }
    }

    // ----------------------------------------------------------------
    // 6) ArchiveDocument_HidesFromActiveList_ButHistoryRemains
    // ----------------------------------------------------------------
    [Fact]
    public async Task ArchiveDocument_HidesFromActiveList_ButHistoryRemains()
    {
        var addHandler = new AddDocumentHandler(_eventStore, _blobStore);
        var archiveHandler = new ArchiveDocumentHandler(_eventStore);
        var listHandler = new GetDocumentsListHandler(_eventStore, _blobStore);
        var bytes = new byte[] { 1 };

        var docId = await addHandler.HandleAsync(
            new AddDocumentCommand(null, "archived.pdf", null, "[]", "",
                new DateOnly(2025, 6, 1), bytes),
            _actorUserId, _deviceId, CancellationToken.None);

        await archiveHandler.HandleAsync(
            new ArchiveDocumentCommand(docId, "Not needed", new DateOnly(2025, 6, 2)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Active list should be empty
        var active = await listHandler.HandleAsync(null, null, null, false, null, CancellationToken.None);
        Assert.Empty(active);

        // Archived list has the doc
        var archived = await listHandler.HandleAsync(null, null, null, true, null, CancellationToken.None);
        Assert.Single(archived);
        Assert.True(archived[0].IsArchived);

        // All list includes it
        var all = await listHandler.HandleAsync(null, null, null, null, null, CancellationToken.None);
        Assert.Single(all);
    }

    // ----------------------------------------------------------------
    // 7) DeterministicProjection_SameEventsSameState
    // ----------------------------------------------------------------
    [Fact]
    public async Task DeterministicProjection_SameEventsSameState()
    {
        var addHandler = new AddDocumentHandler(_eventStore, _blobStore);
        var bytes1 = new byte[] { 1, 2 };
        var bytes2 = new byte[] { 3, 4 };

        await addHandler.HandleAsync(
            new AddDocumentCommand(null, "doc1.pdf", null, "[\"a\"]", "note1",
                new DateOnly(2025, 7, 1), bytes1),
            _actorUserId, _deviceId, CancellationToken.None);

        await addHandler.HandleAsync(
            new AddDocumentCommand(null, "doc2.png", null, "[\"b\"]", "note2",
                new DateOnly(2025, 7, 2), bytes2),
            _actorUserId, _deviceId, CancellationToken.None);

        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);

        var state1 = DocumentVaultProjector.Project(envelopes);
        var state2 = DocumentVaultProjector.Project(envelopes);

        Assert.Equal(state1.Documents.Count, state2.Documents.Count);
        foreach (var kvp in state1.Documents)
        {
            Assert.True(state2.Documents.ContainsKey(kvp.Key));
            Assert.Equal(kvp.Value.FileName, state2.Documents[kvp.Key].FileName);
            Assert.Equal(kvp.Value.Sha256Hex, state2.Documents[kvp.Key].Sha256Hex);
        }
    }

    // ----------------------------------------------------------------
    // 8) BlobIntegrityCheck_DetectsMismatch
    // ----------------------------------------------------------------
    [Fact]
    public async Task BlobIntegrityCheck_DetectsMismatch()
    {
        var bytes = "Integrity test"u8.ToArray();
        var correctHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.True(EncryptedFileDocumentBlobStore.VerifyIntegrity(bytes, correctHash));
        Assert.False(EncryptedFileDocumentBlobStore.VerifyIntegrity(bytes, "0000000000000000000000000000000000000000000000000000000000000000"));
        Assert.False(EncryptedFileDocumentBlobStore.VerifyIntegrity(new byte[] { 99 }, correctHash));
    }

    // ----------------------------------------------------------------
    // 9) AttachmentsForEntity_ReturnsLinkedDocs
    // ----------------------------------------------------------------
    [Fact]
    public async Task AttachmentsForEntity_ReturnsLinkedDocs()
    {
        var addHandler = new AddDocumentHandler(_eventStore, _blobStore);
        var linkHandler = new LinkDocumentHandler(_eventStore);
        var forEntityHandler = new GetDocumentsForEntityHandler(_eventStore, _blobStore);

        var docId1 = await addHandler.HandleAsync(
            new AddDocumentCommand(null, "receipt1.jpg", null, "[]", "",
                new DateOnly(2025, 8, 1), new byte[] { 1 }),
            _actorUserId, _deviceId, CancellationToken.None);

        var docId2 = await addHandler.HandleAsync(
            new AddDocumentCommand(null, "receipt2.jpg", null, "[]", "",
                new DateOnly(2025, 8, 1), new byte[] { 2 }),
            _actorUserId, _deviceId, CancellationToken.None);

        var obligationId = Guid.NewGuid().ToString();
        await linkHandler.HandleAsync(
            new LinkDocumentCommand(docId1, "Obligation", obligationId, "Proof", new DateOnly(2025, 8, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
        await linkHandler.HandleAsync(
            new LinkDocumentCommand(docId2, "Obligation", obligationId, "Statement", new DateOnly(2025, 8, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var attachments = await forEntityHandler.HandleAsync("Obligation", obligationId, CancellationToken.None);
        Assert.Equal(2, attachments.Count);
    }

    // ----------------------------------------------------------------
    // 10) CannotLinkArchivedDocument_Blocked
    // ----------------------------------------------------------------
    [Fact]
    public async Task CannotLinkArchivedDocument_Blocked()
    {
        var addHandler = new AddDocumentHandler(_eventStore, _blobStore);
        var archiveHandler = new ArchiveDocumentHandler(_eventStore);
        var linkHandler = new LinkDocumentHandler(_eventStore);

        var docId = await addHandler.HandleAsync(
            new AddDocumentCommand(null, "old.pdf", null, "[]", "",
                new DateOnly(2025, 9, 1), new byte[] { 1 }),
            _actorUserId, _deviceId, CancellationToken.None);

        await archiveHandler.HandleAsync(
            new ArchiveDocumentCommand(docId, "Obsolete", new DateOnly(2025, 9, 2)),
            _actorUserId, _deviceId, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            linkHandler.HandleAsync(
                new LinkDocumentCommand(docId, "Bill", Guid.NewGuid().ToString(), "Receipt",
                    new DateOnly(2025, 9, 3)),
                _actorUserId, _deviceId, CancellationToken.None));
    }

    // ----------------------------------------------------------------
    // 11) TagsFilter_Works
    // ----------------------------------------------------------------
    [Fact]
    public async Task TagsFilter_Works()
    {
        var addHandler = new AddDocumentHandler(_eventStore, _blobStore);
        var listHandler = new GetDocumentsListHandler(_eventStore, _blobStore);

        await addHandler.HandleAsync(
            new AddDocumentCommand(null, "tagged.pdf", null, "[\"tax\",\"2025\"]", "",
                new DateOnly(2025, 10, 1), new byte[] { 1 }),
            _actorUserId, _deviceId, CancellationToken.None);

        await addHandler.HandleAsync(
            new AddDocumentCommand(null, "other.pdf", null, "[\"general\"]", "",
                new DateOnly(2025, 10, 1), new byte[] { 2 }),
            _actorUserId, _deviceId, CancellationToken.None);

        var taxDocs = await listHandler.HandleAsync("tax", null, null, null, null, CancellationToken.None);
        Assert.Single(taxDocs);
        Assert.Equal("tagged.pdf", taxDocs[0].FileName);
    }

    // ----------------------------------------------------------------
    // 12) MissingBlobFlag_ShowsInList
    // ----------------------------------------------------------------
    [Fact]
    public async Task MissingBlobFlag_ShowsInList()
    {
        var addHandler = new AddDocumentHandler(_eventStore, _blobStore);
        var purgeHandler = new PurgeDocumentBlobHandler(_eventStore, _blobStore);
        var listHandler = new GetDocumentsListHandler(_eventStore, _blobStore);

        var docId = await addHandler.HandleAsync(
            new AddDocumentCommand(null, "purged.pdf", null, "[]", "",
                new DateOnly(2025, 11, 1), new byte[] { 1 }),
            _actorUserId, _deviceId, CancellationToken.None);

        await purgeHandler.HandleAsync(
            new PurgeDocumentBlobCommand(docId, "Space", new DateOnly(2025, 11, 2)),
            _actorUserId, _deviceId, CancellationToken.None);

        var missingBlobs = await listHandler.HandleAsync(null, null, null, null, true, CancellationToken.None);
        Assert.Single(missingBlobs);
        Assert.True(missingBlobs[0].IsBlobPurged);
        Assert.False(missingBlobs[0].BlobExists);
    }
}
