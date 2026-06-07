namespace DebtManager.Desktop.Services;

/// <summary>
/// Service for full vault backup and restore operations.
/// </summary>
public interface IVaultBackupService
{
    /// <summary>
    /// Creates a .dmvault backup file at a user-selected location.
    /// </summary>
    Task BackupAsync(CancellationToken ct);

    /// <summary>
    /// Restores from a user-selected .dmvault file, replacing the current database.
    /// Requires explicit confirmation before proceeding.
    /// Returns the selected file path for confirmation UI, or null if cancelled.
    /// </summary>
    string? PickRestoreFile();

    /// <summary>
    /// Executes the actual restore after user confirmation.
    /// </summary>
    Task RestoreAsync(string dmvaultPath, CancellationToken ct);
}
