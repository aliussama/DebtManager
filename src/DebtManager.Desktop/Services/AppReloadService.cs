namespace DebtManager.Desktop.Services;

/// <summary>
/// Singleton that broadcasts reload requests to all subscribers.
/// </summary>
public sealed class AppReloadService : IAppReloadService
{
    public event EventHandler? ReloadRequested;

    public void RequestReload()
    {
        ReloadRequested?.Invoke(this, EventArgs.Empty);
    }
}
