namespace DebtManager.Desktop.Services;

/// <summary>
/// Toast notification types.
/// </summary>
public enum ToastType
{
    Success,
    Error,
    Info,
    Warning
}

/// <summary>
/// Represents a toast notification message.
/// </summary>
public sealed record ToastMessage(
    Guid Id,
    ToastType Type,
    string Message,
    DateTimeOffset CreatedAt
)
{
    public static ToastMessage Success(string message) =>
        new(Guid.NewGuid(), ToastType.Success, message, DateTimeOffset.Now);

    public static ToastMessage Error(string message) =>
        new(Guid.NewGuid(), ToastType.Error, message, DateTimeOffset.Now);

    public static ToastMessage Info(string message) =>
        new(Guid.NewGuid(), ToastType.Info, message, DateTimeOffset.Now);

    public static ToastMessage Warning(string message) =>
        new(Guid.NewGuid(), ToastType.Warning, message, DateTimeOffset.Now);
}

/// <summary>
/// Interface for toast notification service.
/// </summary>
public interface IToastService
{
    /// <summary>
    /// Fired when a new toast should be displayed.
    /// </summary>
    event Action<ToastMessage>? OnToast;

    /// <summary>
    /// Show a success toast.
    /// </summary>
    void Success(string message);

    /// <summary>
    /// Show an error toast with optional exception details.
    /// </summary>
    void Error(string message, Exception? ex = null);

    /// <summary>
    /// Show an info toast.
    /// </summary>
    void Info(string message);

    /// <summary>
    /// Show a warning toast.
    /// </summary>
    void Warning(string message);
}

/// <summary>
/// Thread-safe toast notification service.
/// Singleton that can be injected into ViewModels.
/// </summary>
public sealed class ToastService : IToastService
{
    private const int MaxMessageLength = 200;

    public event Action<ToastMessage>? OnToast;

    public void Success(string message)
    {
        var toast = ToastMessage.Success(TruncateMessage(message));
        OnToast?.Invoke(toast);
    }

    public void Error(string message, Exception? ex = null)
    {
        var fullMessage = ex != null
            ? $"{message}: {ex.Message}"
            : message;

        var toast = ToastMessage.Error(TruncateMessage(fullMessage));
        OnToast?.Invoke(toast);
    }

    public void Info(string message)
    {
        var toast = ToastMessage.Info(TruncateMessage(message));
        OnToast?.Invoke(toast);
    }

    public void Warning(string message)
    {
        var toast = ToastMessage.Warning(TruncateMessage(message));
        OnToast?.Invoke(toast);
    }

    private static string TruncateMessage(string message)
    {
        // Remove newlines and normalize whitespace
        var normalized = message
            .Replace("\r\n", " ")
            .Replace("\n", " ")
            .Replace("\r", " ");

        // Collapse multiple spaces
        while (normalized.Contains("  "))
            normalized = normalized.Replace("  ", " ");

        normalized = normalized.Trim();

        // Truncate if too long
        if (normalized.Length > MaxMessageLength)
            return normalized[..(MaxMessageLength - 3)] + "...";

        return normalized;
    }
}
