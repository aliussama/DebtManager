using Microsoft.Win32;

namespace DebtManager.Desktop.Services;

/// <summary>
/// Production implementation of IFileDialogService using WPF SaveFileDialog.
/// </summary>
public sealed class FileDialogService : IFileDialogService
{
    public string? ShowSaveFileDialog(string defaultFileName, string filter, string title)
    {
        var dialog = new SaveFileDialog
        {
            FileName = defaultFileName,
            Filter = filter,
            Title = title
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowOpenFileDialog(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            Title = title
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
