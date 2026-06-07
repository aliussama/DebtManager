namespace DebtManager.Desktop.Services;

/// <summary>
/// Routes focus requests from ViewModels to UI elements via events.
/// </summary>
public sealed class FocusRequestService : IFocusRequestService
{
    public event EventHandler<FocusRequestEventArgs>? FocusRequested;

    public void RequestFocus(string targetKey)
    {
        FocusRequested?.Invoke(this, new FocusRequestEventArgs(targetKey));
    }
}
