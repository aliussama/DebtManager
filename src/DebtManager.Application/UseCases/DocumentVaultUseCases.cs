using System.Security.Cryptography;
using System.Text.Json;
using DebtManager.Domain.Documents;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// ????????????????????????????????????????????
// Commands
// ????????????????????????????????????????????

public sealed record AddDocumentCommand(
    Guid? DocumentId,
    string FileName,
    string? MimeType,
    string TagsJson,
    string Notes,
    DateOnly EffectiveDate,
    byte[] FileBytes
);

public sealed record UpdateDocumentMetadataCommand(
    Guid DocumentId,
    string FileName,
    string TagsJson,
    string Notes,
    DateOnly EffectiveDate
);

public sealed record ArchiveDocumentCommand(
    Guid DocumentId,
    string Reason,
    DateOnly EffectiveDate
);

public sealed record PurgeDocumentBlobCommand(
    Guid DocumentId,
    string Reason,
    DateOnly EffectiveDate
);

public sealed record LinkDocumentCommand(
    Guid DocumentId,
    string EntityType,
    string EntityId,
    string LinkRole,
    DateOnly EffectiveDate
);

public sealed record UnlinkDocumentCommand(
    Guid DocumentId,
    string EntityType,
    string EntityId,
    string Reason,
    DateOnly EffectiveDate
);

public sealed record ExportDocumentCommand(
    Guid DocumentId,
    string ExportPath,
    DateOnly EffectiveDate
);

// ????????????????????????????????????????????
// Result records
// ????????????????????????????????????????????

public sealed record DocumentListItem(
    Guid DocumentId,
    string FileName,
    string MimeType,
    long SizeBytes,
    string Sha256Hex,
    string TagsJson,
    string Notes,
    bool IsArchived,
    bool IsBlobPurged,
    bool BlobExists,
    DateOnly CreatedDate
);

public sealed record DocumentsForEntityItem(
    Guid DocumentId,
    string FileName,
    string MimeType,
    long SizeBytes,
    string LinkRole,
    bool IsArchived,
    bool IsBlobPurged,
    bool BlobExists,
    DateOnly CreatedDate
);

// ????????????????????????????????????????????
// Handlers
// ????????????????????????????????????????????

public sealed class AddDocumentHandler
{
    private readonly IEventStore _eventStore;
    private readonly IDocumentBlobStore _blobStore;

    public AddDocumentHandler(IEventStore eventStore, IDocumentBlobStore blobStore)
    {
        _eventStore = eventStore;
        _blobStore = blobStore;
    }

    public async Task<Guid> HandleAsync(
        AddDocumentCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        ValidateTags(cmd.TagsJson);

        var docId = cmd.DocumentId ?? Guid.NewGuid();
        var storageKey = Guid.NewGuid().ToString("N");

        var sha256Hex = Convert.ToHexString(SHA256.HashData(cmd.FileBytes)).ToLowerInvariant();
        var mimeType = cmd.MimeType ?? InferMimeType(cmd.FileName);

        var ev = new DocumentCreated(
            docId, cmd.FileName, mimeType, cmd.FileBytes.Length,
            sha256Hex, storageKey, cmd.TagsJson, cmd.Notes, cmd.EffectiveDate);

        await _eventStore.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(docId),
            nameof(DocumentCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);

        await _blobStore.SaveAsync(storageKey, cmd.FileBytes, ct);

        return docId;
    }

    private static string InferMimeType(string fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName)?.ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".tiff" or ".tif" => "image/tiff",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }

    private static void ValidateTags(string tagsJson)
    {
        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(tagsJson);
            if (arr == null)
                throw new ArgumentException("TagsJson must be a JSON array of strings.");
        }
        catch (JsonException)
        {
            throw new ArgumentException("TagsJson must be a valid JSON array of strings.");
        }
    }
}

public sealed class UpdateDocumentMetadataHandler
{
    private readonly IEventStore _eventStore;

    public UpdateDocumentMetadataHandler(IEventStore eventStore) => _eventStore = eventStore;

    public async Task HandleAsync(
        UpdateDocumentMetadataCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new DocumentMetadataUpdated(cmd.DocumentId, cmd.FileName, cmd.TagsJson, cmd.Notes, cmd.EffectiveDate);
        await _eventStore.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.DocumentId),
            nameof(DocumentMetadataUpdated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class ArchiveDocumentHandler
{
    private readonly IEventStore _eventStore;

    public ArchiveDocumentHandler(IEventStore eventStore) => _eventStore = eventStore;

    public async Task HandleAsync(
        ArchiveDocumentCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new DocumentArchived(cmd.DocumentId, cmd.Reason, cmd.EffectiveDate);
        await _eventStore.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.DocumentId),
            nameof(DocumentArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class PurgeDocumentBlobHandler
{
    private readonly IEventStore _eventStore;
    private readonly IDocumentBlobStore _blobStore;

    public PurgeDocumentBlobHandler(IEventStore eventStore, IDocumentBlobStore blobStore)
    {
        _eventStore = eventStore;
        _blobStore = blobStore;
    }

    public async Task HandleAsync(
        PurgeDocumentBlobCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        // Find the storage key from projection
        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = DocumentVaultProjector.Project(envelopes);

        if (!state.Documents.TryGetValue(cmd.DocumentId, out var doc))
            throw new InvalidOperationException($"Document {cmd.DocumentId} not found.");

        await _blobStore.PurgeAsync(doc.StorageKey, ct);

        var ev = new DocumentBlobPurged(cmd.DocumentId, cmd.Reason, cmd.EffectiveDate);
        await _eventStore.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.DocumentId),
            nameof(DocumentBlobPurged), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class LinkDocumentHandler
{
    private readonly IEventStore _eventStore;

    public LinkDocumentHandler(IEventStore eventStore) => _eventStore = eventStore;

    public async Task HandleAsync(
        LinkDocumentCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.EntityId))
            throw new ArgumentException("EntityId must not be empty.");

        // Validate doc exists and is not archived
        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = DocumentVaultProjector.Project(envelopes);

        if (!state.Documents.TryGetValue(cmd.DocumentId, out var doc))
            throw new InvalidOperationException($"Document {cmd.DocumentId} not found.");

        if (doc.IsArchived)
            throw new InvalidOperationException("Cannot link an archived document.");

        var ev = new DocumentLinked(cmd.DocumentId, cmd.EntityType, cmd.EntityId, cmd.LinkRole, cmd.EffectiveDate);
        await _eventStore.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.DocumentId),
            nameof(DocumentLinked), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class UnlinkDocumentHandler
{
    private readonly IEventStore _eventStore;

    public UnlinkDocumentHandler(IEventStore eventStore) => _eventStore = eventStore;

    public async Task HandleAsync(
        UnlinkDocumentCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new DocumentUnlinked(cmd.DocumentId, cmd.EntityType, cmd.EntityId, cmd.Reason, cmd.EffectiveDate);
        await _eventStore.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.DocumentId),
            nameof(DocumentUnlinked), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class ExportDocumentHandler
{
    private readonly IEventStore _eventStore;
    private readonly IDocumentBlobStore _blobStore;

    public ExportDocumentHandler(IEventStore eventStore, IDocumentBlobStore blobStore)
    {
        _eventStore = eventStore;
        _blobStore = blobStore;
    }

    public async Task HandleAsync(
        ExportDocumentCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = DocumentVaultProjector.Project(envelopes);

        if (!state.Documents.TryGetValue(cmd.DocumentId, out var doc))
            throw new InvalidOperationException($"Document {cmd.DocumentId} not found.");

        var plaintext = await _blobStore.LoadAsync(doc.StorageKey, ct);
        if (plaintext == null)
            throw new InvalidOperationException("Blob not found. It may have been purged.");

        // Verify integrity
        var sha256Hex = Convert.ToHexString(SHA256.HashData(plaintext)).ToLowerInvariant();
        if (!string.Equals(sha256Hex, doc.Sha256Hex, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Integrity check failed: SHA256 mismatch.");

        await System.IO.File.WriteAllBytesAsync(cmd.ExportPath, plaintext, ct);

        var ev = new DocumentExported(cmd.DocumentId, System.IO.Path.GetFileName(cmd.ExportPath), cmd.EffectiveDate);
        await _eventStore.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.DocumentId),
            nameof(DocumentExported), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class GetDocumentVaultDashboardHandler
{
    private readonly IEventStore _eventStore;

    public GetDocumentVaultDashboardHandler(IEventStore eventStore) => _eventStore = eventStore;

    public async Task<DocumentVaultSummary> HandleAsync(CancellationToken ct)
    {
        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = DocumentVaultProjector.Project(envelopes);
        return state.GetSummary();
    }
}

public sealed class GetDocumentsListHandler
{
    private readonly IEventStore _eventStore;
    private readonly IDocumentBlobStore _blobStore;

    public GetDocumentsListHandler(IEventStore eventStore, IDocumentBlobStore blobStore)
    {
        _eventStore = eventStore;
        _blobStore = blobStore;
    }

    public async Task<List<DocumentListItem>> HandleAsync(
        string? tagFilter, string? textFilter, string? mimeFilter,
        bool? archivedOnly, bool? missingBlobOnly, CancellationToken ct)
    {
        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = DocumentVaultProjector.Project(envelopes);

        var docs = state.Documents.Values.AsEnumerable();

        if (archivedOnly == true)
            docs = docs.Where(d => d.IsArchived);
        else if (archivedOnly == false)
            docs = docs.Where(d => !d.IsArchived);

        if (!string.IsNullOrWhiteSpace(mimeFilter))
            docs = docs.Where(d => d.MimeType.Contains(mimeFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(textFilter))
        {
            docs = docs.Where(d =>
                d.FileName.Contains(textFilter, StringComparison.OrdinalIgnoreCase) ||
                d.Notes.Contains(textFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(tagFilter))
        {
            docs = docs.Where(d => d.TagsJson.Contains(tagFilter, StringComparison.OrdinalIgnoreCase));
        }

        var result = new List<DocumentListItem>();
        foreach (var d in docs)
        {
            var blobExists = !d.IsBlobPurged && await _blobStore.ExistsAsync(d.StorageKey, ct);

            if (missingBlobOnly == true && blobExists)
                continue;

            result.Add(new DocumentListItem(
                d.DocumentId, d.FileName, d.MimeType, d.SizeBytes,
                d.Sha256Hex, d.TagsJson, d.Notes,
                d.IsArchived, d.IsBlobPurged, blobExists, d.CreatedDate));
        }

        return result;
    }
}

public sealed class GetDocumentsForEntityHandler
{
    private readonly IEventStore _eventStore;
    private readonly IDocumentBlobStore _blobStore;

    public GetDocumentsForEntityHandler(IEventStore eventStore, IDocumentBlobStore blobStore)
    {
        _eventStore = eventStore;
        _blobStore = blobStore;
    }

    public async Task<List<DocumentsForEntityItem>> HandleAsync(
        string entityType, string entityId, CancellationToken ct)
    {
        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = DocumentVaultProjector.Project(envelopes);

        var entityKey = $"{entityType}:{entityId}";
        if (!state.LinksByEntity.TryGetValue(entityKey, out var links))
            return new List<DocumentsForEntityItem>();

        var result = new List<DocumentsForEntityItem>();
        foreach (var link in links)
        {
            if (!state.Documents.TryGetValue(link.DocumentId, out var doc))
                continue;

            var blobExists = !doc.IsBlobPurged && await _blobStore.ExistsAsync(doc.StorageKey, ct);
            result.Add(new DocumentsForEntityItem(
                doc.DocumentId, doc.FileName, doc.MimeType, doc.SizeBytes,
                link.LinkRole, doc.IsArchived, doc.IsBlobPurged, blobExists, doc.CreatedDate));
        }

        return result;
    }
}
