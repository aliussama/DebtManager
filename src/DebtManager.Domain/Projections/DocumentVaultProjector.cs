using System.Text.Json;
using DebtManager.Domain.Documents;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

public static class DocumentVaultProjector
{
    public static DocumentVaultState Project(IEnumerable<EventEnvelope> envelopes)
    {
        var state = new DocumentVaultState();
        var opt = DomainJson.Options;

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value);

        foreach (var env in ordered)
        {
            switch (env.EventType)
            {
                case nameof(DocumentCreated):
                {
                    var ev = JsonSerializer.Deserialize<DocumentCreated>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Documents[ev.DocumentId] = new DocumentRecord
                    {
                        DocumentId = ev.DocumentId,
                        FileName = ev.FileName,
                        MimeType = ev.MimeType,
                        SizeBytes = ev.SizeBytes,
                        Sha256Hex = ev.Sha256Hex,
                        StorageKey = ev.StorageKey,
                        TagsJson = ev.TagsJson,
                        Notes = ev.Notes,
                        IsArchived = false,
                        IsBlobPurged = false,
                        CreatedDate = ev.EffectiveDate
                    };
                    break;
                }
                case nameof(DocumentMetadataUpdated):
                {
                    var ev = JsonSerializer.Deserialize<DocumentMetadataUpdated>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Documents.TryGetValue(ev.DocumentId, out var doc))
                    {
                        doc.FileName = ev.FileName;
                        doc.TagsJson = ev.TagsJson;
                        doc.Notes = ev.Notes;
                    }
                    break;
                }
                case nameof(DocumentArchived):
                {
                    var ev = JsonSerializer.Deserialize<DocumentArchived>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Documents.TryGetValue(ev.DocumentId, out var doc))
                        doc.IsArchived = true;
                    break;
                }
                case nameof(DocumentBlobPurged):
                {
                    var ev = JsonSerializer.Deserialize<DocumentBlobPurged>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Documents.TryGetValue(ev.DocumentId, out var doc))
                        doc.IsBlobPurged = true;
                    break;
                }
                case nameof(DocumentLinked):
                {
                    var ev = JsonSerializer.Deserialize<DocumentLinked>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    var link = new DocumentLinkRecord(ev.DocumentId, ev.EntityType, ev.EntityId, ev.LinkRole);
                    var entityKey = $"{ev.EntityType}:{ev.EntityId}";

                    if (!state.LinksByEntity.ContainsKey(entityKey))
                        state.LinksByEntity[entityKey] = new List<DocumentLinkRecord>();
                    state.LinksByEntity[entityKey].Add(link);

                    if (!state.LinksByDocument.ContainsKey(ev.DocumentId))
                        state.LinksByDocument[ev.DocumentId] = new List<DocumentLinkRecord>();
                    state.LinksByDocument[ev.DocumentId].Add(link);
                    break;
                }
                case nameof(DocumentUnlinked):
                {
                    var ev = JsonSerializer.Deserialize<DocumentUnlinked>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    var entityKey = $"{ev.EntityType}:{ev.EntityId}";

                    if (state.LinksByEntity.TryGetValue(entityKey, out var entityLinks))
                        entityLinks.RemoveAll(l => l.DocumentId == ev.DocumentId);

                    if (state.LinksByDocument.TryGetValue(ev.DocumentId, out var docLinks))
                        docLinks.RemoveAll(l => l.EntityType == ev.EntityType && l.EntityId == ev.EntityId);
                    break;
                }
            }
        }

        return state;
    }
}
