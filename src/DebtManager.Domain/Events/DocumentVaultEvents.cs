namespace DebtManager.Domain.Events;

public sealed record DocumentCreated(
    Guid DocumentId,
    string FileName,
    string MimeType,
    long SizeBytes,
    string Sha256Hex,
    string StorageKey,
    string TagsJson,
    string Notes,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record DocumentMetadataUpdated(
    Guid DocumentId,
    string FileName,
    string TagsJson,
    string Notes,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record DocumentArchived(
    Guid DocumentId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record DocumentBlobPurged(
    Guid DocumentId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record DocumentLinked(
    Guid DocumentId,
    string EntityType,
    string EntityId,
    string LinkRole,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record DocumentUnlinked(
    Guid DocumentId,
    string EntityType,
    string EntityId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record DocumentExported(
    Guid DocumentId,
    string ExportPathHint,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
