namespace DebtManager.Desktop.Services;

/// <summary>
/// Event args for focus request events.
/// </summary>
public sealed class FocusRequestEventArgs : EventArgs
{
    public string TargetKey { get; }

    public FocusRequestEventArgs(string targetKey)
    {
        TargetKey = targetKey;
    }
}

/// <summary>
/// Service that routes focus requests from ViewModels to UI elements
/// without coupling them directly.
/// </summary>
public interface IFocusRequestService
{
    /// <summary>
    /// Fired when a focus request is made for a specific target key.
    /// </summary>
    event EventHandler<FocusRequestEventArgs>? FocusRequested;

    /// <summary>
    /// Request that the UI element registered with the given key receives focus.
    /// </summary>
    void RequestFocus(string targetKey);
}
