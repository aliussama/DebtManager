namespace DebtManager.Desktop.Recovery;

/// <summary>
/// Persisted crash marker written at startup, cleared on clean exit.
/// If present on next start it indicates the previous run did not exit cleanly.
/// </summary>
public sealed class CrashMarker
{
    public DateTimeOffset Timestamp { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string LastAction { get; set; } = string.Empty;
    public string LastView { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
}

/// <summary>
/// Decision made after evaluating a crash marker.
/// </summary>
public enum RecoveryDecision
{
    NormalStart,
    SafeMode,
    RepairPrompt
}
