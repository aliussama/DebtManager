using System.IO;
using System.Text.Json;
using DebtManager.Infrastructure.Diagnostics;

namespace DebtManager.Desktop.Recovery;

/// <summary>
/// Detects crash markers from a previous unclean shutdown and determines
/// whether to start in normal mode or safe mode. Deterministic, local-only.
/// </summary>
public sealed class CrashRecoveryService
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DebtManager");

    private static readonly string MarkerPath = Path.Combine(DataDir, "crash_marker.json");

    /// <summary>
    /// Checks whether a crash marker exists from a previous unclean shutdown.
    /// Returns the marker if found, null otherwise.
    /// </summary>
    public CrashMarker? DetectCrashMarker()
    {
        try
        {
            if (!File.Exists(MarkerPath))
                return null;

            var json = File.ReadAllText(MarkerPath);
            var marker = JsonSerializer.Deserialize<CrashMarker>(json);
            return marker;
        }
        catch (Exception ex)
        {
            AppDiagnostics.WriteWarn("Recovery", $"Failed to read crash marker: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Evaluates the crash marker and returns a recovery decision.
    /// </summary>
    public RecoveryDecision Evaluate(CrashMarker? marker)
    {
        if (marker == null)
            return RecoveryDecision.NormalStart;

        // If the crash happened very recently (within 10 seconds of startup),
        // it likely indicates a startup crash loop — suggest safe mode.
        if (marker.LastAction == "Startup" || marker.LastAction == "UnhandledException")
            return RecoveryDecision.SafeMode;

        // For other crashes, offer safe mode.
        return RecoveryDecision.SafeMode;
    }

    /// <summary>
    /// Opens the logs folder in the system file explorer.
    /// </summary>
    public static void OpenLogsFolder()
    {
        try
        {
            var logsDir = AppDiagnostics.LogsDirectory;
            if (Directory.Exists(logsDir))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = logsDir,
                    UseShellExecute = true
                });
        }
        catch { /* best-effort */ }
    }
}
