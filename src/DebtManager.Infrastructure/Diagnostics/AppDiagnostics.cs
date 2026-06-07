using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace DebtManager.Infrastructure.Diagnostics;

/// <summary>
/// Local-only rolling diagnostics logger with correlation id tracking.
/// Writes structured log lines to AppData/DebtManager/logs/.
/// No network, no telemetry — privacy-first.
/// </summary>
public static class AppDiagnostics
{
    private static readonly object _lock = new();
    private static string? _logsDir;
    private static string? _currentCorrelationId;
    private static Guid? _currentVaultId;

    public static string LogsDirectory
    {
        get
        {
            if (_logsDir == null)
            {
                _logsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DebtManager", "logs");
                Directory.CreateDirectory(_logsDir);
            }
            return _logsDir;
        }
    }

    /// <summary>
    /// Override the logs directory (for testing).
    /// </summary>
    public static void SetLogsDirectory(string dir)
    {
        _logsDir = dir;
        Directory.CreateDirectory(dir);
    }

    public static string NewCorrelationId()
    {
        var id = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        _currentCorrelationId = id;
        return id;
    }

    public static void SetCurrentCorrelationId(string correlationId) =>
        _currentCorrelationId = correlationId;

    public static string GetCurrentCorrelationId() =>
        _currentCorrelationId ?? NewCorrelationId();

    public static void SetCurrentVaultId(Guid? vaultId) =>
        _currentVaultId = vaultId;

    public static void WriteInfo(string area, string message, string? correlationId = null) =>
        Write("INFO", area, message, correlationId);

    public static void WriteWarn(string area, string message, string? correlationId = null) =>
        Write("WARN", area, message, correlationId);

    public static void WriteError(string area, Exception ex, string? correlationId = null, string? extraContextJson = null)
    {
        var sb = new StringBuilder();
        sb.Append(ex.GetType().Name).Append(": ").Append(ex.Message);
        if (ex.StackTrace != null)
            sb.Append(" | StackTrace: ").Append(ex.StackTrace.Replace("\r\n", " ? ").Replace("\n", " ? "));
        if (!string.IsNullOrEmpty(extraContextJson))
            sb.Append(" | Context: ").Append(extraContextJson);
        Write("ERROR", area, sb.ToString(), correlationId);
    }

    public static string GetAppVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? asm.GetName().Version?.ToString()
            ?? "1.0.0";
    }

    public static string GetOsVersion() =>
        $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

    /// <summary>
    /// Writes a crash marker file used by CrashRecoveryService on next startup.
    /// </summary>
    public static void WriteCrashMarker(string correlationId, string lastAction, string lastView)
    {
        try
        {
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DebtManager");
            Directory.CreateDirectory(dataDir);
            var markerPath = Path.Combine(dataDir, "crash_marker.json");
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = correlationId,
                LastAction = lastAction,
                LastView = lastView,
                AppVersion = GetAppVersion()
            });
            File.WriteAllText(markerPath, json);
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Clears the crash marker on clean exit.
    /// </summary>
    public static void ClearCrashMarker()
    {
        try
        {
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DebtManager");
            var markerPath = Path.Combine(dataDir, "crash_marker.json");
            if (File.Exists(markerPath))
                File.Delete(markerPath);
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Prune log files older than 30 days.
    /// </summary>
    public static void PruneOldLogs(int retainDays = 30)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-retainDays);
            foreach (var file in Directory.GetFiles(LogsDirectory, "*.log"))
            {
                if (File.GetCreationTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch { /* best-effort */ }
    }

    private static void Write(string level, string area, string message, string? correlationId)
    {
        var cid = correlationId ?? _currentCorrelationId ?? "NONE";
        var vaultPrefix = _currentVaultId.HasValue ? $"[VaultId={_currentVaultId.Value}] " : "";
        var line = $"{DateTimeOffset.UtcNow:O} [{level}] [{cid}] [{area}] {vaultPrefix}{message}";
        var logFile = Path.Combine(LogsDirectory, $"debtmanager_{DateTime.UtcNow:yyyyMMdd}.log");

        lock (_lock)
        {
            try
            {
                File.AppendAllText(logFile, line + Environment.NewLine);
            }
            catch { /* never crash on logging */ }
        }
    }
}
