namespace DebtManager.Domain.Documents;

public enum DocumentStatus
{
    Active,
    Archived
}

public sealed record DocumentLinkRecord(
    Guid DocumentId,
    string EntityType,
    string EntityId,
    string LinkRole
);

public sealed class DocumentRecord
{
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256Hex { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public string TagsJson { get; set; } = "[]";
    public string Notes { get; set; } = "";
    public bool IsArchived { get; set; }
    public bool IsBlobPurged { get; set; }
    public DateOnly CreatedDate { get; set; }
}

public sealed record DocumentVaultSummary(
    int TotalDocs,
    int ActiveDocs,
    int ArchivedDocs,
    int MissingBlobsCount,
    long TotalSizeBytes
);
