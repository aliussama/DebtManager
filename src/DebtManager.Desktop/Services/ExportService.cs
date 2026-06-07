using System.IO;
using System.Text;

namespace DebtManager.Desktop.Services;

/// <summary>
/// Service for exporting data to files.
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Exports data to CSV file.
    /// Shows SaveFileDialog, writes data if user confirms, shows toast on success/error.
    /// Returns silently if user cancels.
    /// </summary>
    Task ExportCsvAsync(
        string defaultFileName,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string?>> rows,
        CancellationToken ct = default);

    /// <summary>
    /// Exports data to CSV file at specified path (for testing).
    /// Does not show file dialog.
    /// </summary>
    Task ExportCsvToPathAsync(
        string filePath,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string?>> rows,
        CancellationToken ct = default);

    /// <summary>
    /// Exports custom CSV content via a writer callback.
    /// Shows SaveFileDialog, invokes callback if user confirms, shows toast on success/error.
    /// </summary>
    Task ExportCustomCsvAsync(
        string defaultFileName,
        Action<TextWriter> writeContent,
        CancellationToken ct = default);

    /// <summary>
    /// Exports custom CSV content to a specific path (for testing).
    /// </summary>
    Task ExportCustomCsvToPathAsync(
        string filePath,
        Action<TextWriter> writeContent,
        CancellationToken ct = default);
}

/// <summary>
/// Production implementation of IExportService.
/// Uses SaveFileDialog for file selection and CsvWriter for formatting.
/// </summary>
public sealed class ExportService : IExportService
{
    private readonly IFileDialogService _fileDialogService;
    private readonly IToastService? _toastService;

    public ExportService(IFileDialogService fileDialogService, IToastService? toastService = null)
    {
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        _toastService = toastService;
    }

    public async Task ExportCsvAsync(
        string defaultFileName,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string?>> rows,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        // Show save file dialog
        var filePath = _fileDialogService.ShowSaveFileDialog(
            defaultFileName,
            "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            "Export to CSV");

        // User cancelled - return silently (no error toast)
        if (string.IsNullOrEmpty(filePath))
            return;

        try
        {
            await ExportCsvToPathAsync(filePath, headers, rows, ct);

            var fileName = Path.GetFileName(filePath);
            _toastService?.Success($"Exported to {fileName}");
        }
        catch (OperationCanceledException)
        {
            // Cancelled - return silently
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to export CSV", ex);
        }
    }

    public Task ExportCsvToPathAsync(
        string filePath,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string?>> rows,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        ct.ThrowIfCancellationRequested();

        // Materialize rows to avoid multiple enumeration
        var rowsList = rows.ToList();

        // Write to file using UTF-8 without BOM
        using var writer = new StreamWriter(filePath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        CsvWriter.Write(writer, headers, rowsList);

        return Task.CompletedTask;
    }

    public async Task ExportCustomCsvAsync(
        string defaultFileName,
        Action<TextWriter> writeContent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(writeContent);

        var filePath = _fileDialogService.ShowSaveFileDialog(
            defaultFileName,
            "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            "Export to CSV");

        if (string.IsNullOrEmpty(filePath))
            return;

        try
        {
            await ExportCustomCsvToPathAsync(filePath, writeContent, ct);

            var fileName = Path.GetFileName(filePath);
            _toastService?.Success($"Exported to {fileName}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to export CSV", ex);
        }
    }

    public Task ExportCustomCsvToPathAsync(
        string filePath,
        Action<TextWriter> writeContent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(writeContent);

        ct.ThrowIfCancellationRequested();

        using var writer = new StreamWriter(filePath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writeContent(writer);

        return Task.CompletedTask;
    }
}
