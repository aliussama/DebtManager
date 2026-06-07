namespace DebtManager.Domain.Identity;

public enum VaultUserRole
{
    Owner,
    Admin,
    Accountant,
    Auditor,
    Viewer
}

public static class VaultPermission
{
    public const string PERM_READ_ALL = "PERM_READ_ALL";
    public const string PERM_WRITE_CASH = "PERM_WRITE_CASH";
    public const string PERM_WRITE_BILLING = "PERM_WRITE_BILLING";
    public const string PERM_WRITE_IMPORT = "PERM_WRITE_IMPORT";
    public const string PERM_WRITE_ASSETS = "PERM_WRITE_ASSETS";
    public const string PERM_WRITE_INVESTMENTS = "PERM_WRITE_INVESTMENTS";
    public const string PERM_WRITE_TAX = "PERM_WRITE_TAX";
    public const string PERM_WRITE_PLANNING = "PERM_WRITE_PLANNING";
    public const string PERM_MANAGE_USERS = "PERM_MANAGE_USERS";
    public const string PERM_MANAGE_SETTINGS = "PERM_MANAGE_SETTINGS";
    public const string PERM_EXPORT_DATA = "PERM_EXPORT_DATA";
    public const string PERM_PURGE_BLOBS = "PERM_PURGE_BLOBS";
    public const string PERM_APPROVE_AI_PROPOSALS = "PERM_APPROVE_AI_PROPOSALS";
    public const string PERM_RUN_AI_ANALYSIS = "PERM_RUN_AI_ANALYSIS";
    public const string PERM_RUN_DATA_QUALITY = "PERM_RUN_DATA_QUALITY";
    public const string PERM_APPLY_DATA_FIXES = "PERM_APPLY_DATA_FIXES";
}

public sealed class VaultUserRecord
{
    public Guid UserId { get; set; }
    public Guid VaultId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public VaultUserRole Role { get; set; } = VaultUserRole.Viewer;
    public bool IsActive { get; set; } = true;
    public bool IsArchived { get; set; }
    public string? ArchiveReason { get; set; }
    public string? PasswordHashBase64 { get; set; }
    public string? PasswordSaltBase64 { get; set; }
    public int PasswordAlgoVersion { get; set; }
    public DateOnly CreatedDate { get; set; }
    public DateOnly LastModifiedDate { get; set; }
}

public sealed class UserSessionRecord
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public Guid DeviceId { get; set; }
    public string ClientVersion { get; set; } = string.Empty;
    public DateOnly StartedDate { get; set; }
    public DateOnly? EndedDate { get; set; }
    public string? EndReason { get; set; }
    public bool IsActive => EndedDate == null;
}

public sealed record IdentitySummary(
    int ActiveUserCount,
    int ActiveSessionsCount,
    bool HasOwner);
