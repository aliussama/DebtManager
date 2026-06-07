namespace DebtManager.Desktop.Services;

/// <summary>
/// Service that signals an app-wide data reload (e.g. after vault restore).
/// </summary>
public interface IAppReloadService
{
    /// <summary>
    /// Raised when all views should reload their data from the database.
    /// </summary>
    event EventHandler? ReloadRequested;

    /// <summary>
    /// Triggers a full reload across the application.
    /// </summary>
    void RequestReload();
}
