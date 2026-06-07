using DebtManager.Desktop.Help;
using DebtManager.Infrastructure.Diagnostics;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class HelpViewModel : ObservableObject
{
    public HelpViewModel()
    {
        Articles = new ObservableCollection<HelpArticles.HelpArticle>(HelpArticles.All);
        SearchCommand = new RelayCommand(DoSearch);
        CopyDiagnosticsCommand = new RelayCommand(CopyDiagnostics);
        OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);
    }

    public ObservableCollection<HelpArticles.HelpArticle> Articles { get; }

    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
                DoSearch();
        }
    }

    private HelpArticles.HelpArticle? _selectedArticle;
    public HelpArticles.HelpArticle? SelectedArticle
    {
        get => _selectedArticle;
        set => SetProperty(ref _selectedArticle, value);
    }

    private string _diagnosticsText = string.Empty;
    public string DiagnosticsText
    {
        get => _diagnosticsText;
        set => SetProperty(ref _diagnosticsText, value);
    }

    public ICommand SearchCommand { get; }
    public ICommand CopyDiagnosticsCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }

    private void DoSearch()
    {
        var results = HelpArticles.Search(_searchQuery);
        Articles.Clear();
        foreach (var article in results)
            Articles.Add(article);

        if (Articles.Count > 0 && SelectedArticle == null)
            SelectedArticle = Articles[0];
    }

    private void CopyDiagnostics()
    {
        var text = $"App Version: {AppDiagnostics.GetAppVersion()}\n"
            + $"OS: {AppDiagnostics.GetOsVersion()}\n"
            + $"Logs Directory: {AppDiagnostics.LogsDirectory}\n"
            + $"Last Correlation ID: {AppDiagnostics.GetCurrentCorrelationId()}\n"
            + $"Timestamp: {DateTimeOffset.UtcNow:O}";

        DiagnosticsText = text;

        try
        {
            Clipboard.SetText(text);
        }
        catch { /* clipboard access can fail in some contexts */ }
    }

    private static void OpenLogsFolder()
    {
        Desktop.Recovery.CrashRecoveryService.OpenLogsFolder();
    }
}
