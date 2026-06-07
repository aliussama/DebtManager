using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Quality;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class DataQualityViewModel : ObservableObject
{
    private readonly RunDataQualityScanHandler? _scanHandler;
    private readonly GetDataQualityDashboardHandler? _dashboardHandler;
    private readonly GetDataQualityIssuesHandler? _issuesHandler;
    private readonly AcknowledgeIssueHandler? _acknowledgeHandler;
    private readonly ResolveIssueHandler? _resolveHandler;
    private readonly PreviewFixHandler? _previewHandler;
    private readonly ApplyFixHandler? _applyHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;

    public DataQualityViewModel(
        RunDataQualityScanHandler? scanHandler = null,
        GetDataQualityDashboardHandler? dashboardHandler = null,
        GetDataQualityIssuesHandler? issuesHandler = null,
        AcknowledgeIssueHandler? acknowledgeHandler = null,
        ResolveIssueHandler? resolveHandler = null,
        PreviewFixHandler? previewHandler = null,
        ApplyFixHandler? applyHandler = null,
        Guid actorUserId = default,
        Guid deviceId = default,
        IToastService? toastService = null,
        IExportService? exportService = null)
    {
        _scanHandler = scanHandler;
        _dashboardHandler = dashboardHandler;
        _issuesHandler = issuesHandler;
        _acknowledgeHandler = acknowledgeHandler;
        _resolveHandler = resolveHandler;
        _previewHandler = previewHandler;
        _applyHandler = applyHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;

        RunScanCommand = new AsyncRelayCommand(RunScanAsync);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        AcknowledgeCommand = new RelayCommand<DataQualityIssue>(i => _ = AcknowledgeAsync(i));
        ResolveCommand = new RelayCommand<DataQualityIssue>(i => _ = ResolveAsync(i));
        PreviewFixCommand = new RelayCommand<DataQualityIssue>(i => _ = PreviewFixAsync(i));
        ApplyFixCommand = new RelayCommand<DataQualityIssue>(i => _ = ApplyFixAsync(i));
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync, () => Issues.Count > 0);

        SeverityOptions = new ObservableCollection<string> { "All", "Critical", "Error", "Warning", "Info" };
        AreaOptions = new ObservableCollection<string>
        {
            "All", "Cash", "Debts", "BankImport", "Budgets", "Recurring",
            "Assets", "Investments", "Taxes", "Goals", "Retirement", "Setup"
        };
        SelectedSeverity = "All";
        SelectedArea = "All";
    }

    public ICommand RunScanCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand AcknowledgeCommand { get; }
    public ICommand ResolveCommand { get; }
    public ICommand PreviewFixCommand { get; }
    public ICommand ApplyFixCommand { get; }
    public ICommand ExportCsvCommand { get; }

    public ObservableCollection<DataQualityIssue> Issues { get; } = new();
    public ObservableCollection<string> SeverityOptions { get; }
    public ObservableCollection<string> AreaOptions { get; }

    private int _totalIssues;
    public int TotalIssues { get => _totalIssues; set => SetProperty(ref _totalIssues, value); }

    private int _criticalCount;
    public int CriticalCount { get => _criticalCount; set => SetProperty(ref _criticalCount, value); }

    private int _errorCount;
    public int ErrorCount { get => _errorCount; set => SetProperty(ref _errorCount, value); }

    private int _warningCount;
    public int WarningCount { get => _warningCount; set => SetProperty(ref _warningCount, value); }

    private int _infoCount;
    public int InfoCount { get => _infoCount; set => SetProperty(ref _infoCount, value); }

    private string? _lastScanDisplay;
    public string? LastScanDisplay { get => _lastScanDisplay; set => SetProperty(ref _lastScanDisplay, value); }

    private string _selectedSeverity = "All";
    public string SelectedSeverity
    {
        get => _selectedSeverity;
        set { if (SetProperty(ref _selectedSeverity, value)) _ = LoadAsync(); }
    }

    private string _selectedArea = "All";
    public string SelectedArea
    {
        get => _selectedArea;
        set { if (SetProperty(ref _selectedArea, value)) _ = LoadAsync(); }
    }

    private string? _searchText;
    public string? SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) _ = LoadAsync(); }
    }

    private bool _onlyUnresolved = true;
    public bool OnlyUnresolved
    {
        get => _onlyUnresolved;
        set { if (SetProperty(ref _onlyUnresolved, value)) _ = LoadAsync(); }
    }

    private DataQualityIssue? _selectedIssue;
    public DataQualityIssue? SelectedIssue
    {
        get => _selectedIssue;
        set
        {
            if (SetProperty(ref _selectedIssue, value))
            {
                OnPropertyChanged(nameof(HasSelectedIssue));
                PreviewText = null;
            }
        }
    }

    public bool HasSelectedIssue => SelectedIssue != null;

    private string? _previewText;
    public string? PreviewText { get => _previewText; set => SetProperty(ref _previewText, value); }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private string? _errorMessage;
    public string? ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    private bool _showConfirmDialog;
    public bool ShowConfirmDialog { get => _showConfirmDialog; set => SetProperty(ref _showConfirmDialog, value); }

    private DataQualityIssue? _pendingFixIssue;
    private string? _pendingFixKind;

    public async Task LoadAsync()
    {
        if (_dashboardHandler == null) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var dashboard = await _dashboardHandler.HandleAsync(CancellationToken.None);

            TotalIssues = dashboard.ActiveIssues.Count;
            CriticalCount = dashboard.CriticalCount;
            ErrorCount = dashboard.ErrorCount;
            WarningCount = dashboard.WarningCount;
            InfoCount = dashboard.InfoCount;
            LastScanDisplay = dashboard.LastScanTime.HasValue
                ? $"Last scan: {dashboard.LastScanTime.Value:yyyy-MM-dd HH:mm}"
                : "Never scanned";

            // Apply filters
            if (_issuesHandler != null)
            {
                DataQualitySeverity? sev = SelectedSeverity != "All"
                    ? Enum.Parse<DataQualitySeverity>(SelectedSeverity) : null;
                DataQualityArea? area = SelectedArea != "All"
                    ? Enum.Parse<DataQualityArea>(SelectedArea) : null;

                var filtered = await _issuesHandler.HandleAsync(
                    new DataQualityIssuesQuery(sev, area, SearchText, OnlyUnresolved),
                    CancellationToken.None);

                Issues.Clear();
                foreach (var issue in filtered)
                    Issues.Add(issue);
            }
            else
            {
                Issues.Clear();
                foreach (var issue in dashboard.ActiveIssues)
                    Issues.Add(issue);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RunScanAsync()
    {
        if (_scanHandler == null) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var summary = await _scanHandler.HandleAsync(_actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success($"Scan complete: {summary.TotalIssues} issue(s) found");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Scan failed: {ex.Message}";
            _toastService?.Error($"Scan failed: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task AcknowledgeAsync(DataQualityIssue? issue)
    {
        if (issue == null || _acknowledgeHandler == null) return;

        try
        {
            await _acknowledgeHandler.HandleAsync(
                new AcknowledgeIssueCommand(issue.IssueId, "Acknowledged by user"),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Issue acknowledged");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error($"Failed: {ex.Message}");
        }
    }

    private async Task ResolveAsync(DataQualityIssue? issue)
    {
        if (issue == null || _resolveHandler == null) return;

        try
        {
            await _resolveHandler.HandleAsync(
                new ResolveIssueCommand(issue.IssueId, "ManualResolve", "{}"),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Issue resolved");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error($"Failed: {ex.Message}");
        }
    }

    private async Task PreviewFixAsync(DataQualityIssue? issue)
    {
        if (issue == null || _previewHandler == null) return;

        var fix = issue.SuggestedFixes.FirstOrDefault(f => !f.RequiresUserInput);
        if (fix == null)
        {
            PreviewText = "No auto-fix available for this issue.";
            return;
        }

        try
        {
            var result = await _previewHandler.HandleAsync(
                new PreviewFixCommand(issue.IssueId, fix.FixKind),
                CancellationToken.None);
            PreviewText = $"{result.PreviewDescription}\n\n{result.EventsSummary}";

            if (result.CanApply)
            {
                _pendingFixIssue = issue;
                _pendingFixKind = fix.FixKind;
                ShowConfirmDialog = true;
            }
        }
        catch (Exception ex)
        {
            PreviewText = $"Preview failed: {ex.Message}";
        }
    }

    private async Task ApplyFixAsync(DataQualityIssue? issue)
    {
        if (_applyHandler == null) return;

        var targetIssue = issue ?? _pendingFixIssue;
        var fixKind = _pendingFixKind;

        if (targetIssue == null) return;
        if (string.IsNullOrEmpty(fixKind))
        {
            var fix = targetIssue.SuggestedFixes.FirstOrDefault(f => !f.RequiresUserInput);
            fixKind = fix?.FixKind;
        }

        if (string.IsNullOrEmpty(fixKind)) return;

        ShowConfirmDialog = false;
        _pendingFixIssue = null;
        _pendingFixKind = null;

        try
        {
            await _applyHandler.HandleAsync(
                new ApplyFixCommand(targetIssue.IssueId, fixKind),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Fix applied successfully");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error($"Fix failed: {ex.Message}");
        }
    }

    private async Task ExportCsvAsync()
    {
        if (_exportService == null || Issues.Count == 0) return;

        var headers = new[] { "Code", "Severity", "Area", "Title", "Description", "Detected At" };
        var rows = Issues.Select(i => new[]
        {
            i.Code, i.Severity.ToString(), i.Area.ToString(),
            i.Title, i.Description, i.DetectedAt.ToString("yyyy-MM-dd HH:mm")
        });

        await _exportService.ExportCsvAsync("DataQualityIssues", headers, rows);
    }
}
