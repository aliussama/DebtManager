namespace DebtManager.Desktop.Services;

/// <summary>
/// Abstraction for file dialogs to enable testability.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Shows a save file dialog and returns the selected path.
    /// Returns null if the user cancels.
    /// </summary>
    string? ShowSaveFileDialog(string defaultFileName, string filter, string title);

    /// <summary>
    /// Shows an open file dialog and returns the selected path.
    /// Returns null if the user cancels.
    /// </summary>
    string? ShowOpenFileDialog(string title, string filter);
}
