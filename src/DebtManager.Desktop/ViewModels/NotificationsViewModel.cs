using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class NotificationsViewModel : ObservableObject
{
    private readonly GetNotificationCenterHandler? _centerHandler;
    private readonly GetNotificationRulesHandler? _rulesHandler;
    private readonly CreateNotificationRuleHandler? _createRuleHandler;
    private readonly ModifyNotificationRuleHandler? _modifyRuleHandler;
    private readonly ArchiveNotificationRuleHandler? _archiveRuleHandler;
    private readonly AcknowledgeNotificationHandler? _acknowledgeHandler;
    private readonly DismissNotificationHandler? _dismissHandler;
    private readonly SnoozeNotificationHandler? _snoozeHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;

    public NotificationsViewModel(
        GetNotificationCenterHandler? centerHandler = null,
        GetNotificationRulesHandler? rulesHandler = null,
        CreateNotificationRuleHandler? createRuleHandler = null,
        ModifyNotificationRuleHandler? modifyRuleHandler = null,
        ArchiveNotificationRuleHandler? archiveRuleHandler = null,
        AcknowledgeNotificationHandler? acknowledgeHandler = null,
        DismissNotificationHandler? dismissHandler = null,
        SnoozeNotificationHandler? snoozeHandler = null,
        Guid actorUserId = default,
        Guid deviceId = default,
        IToastService? toastService = null,
        IExportService? exportService = null)
    {
        _centerHandler = centerHandler;
        _rulesHandler = rulesHandler;
        _createRuleHandler = createRuleHandler;
        _modifyRuleHandler = modifyRuleHandler;
        _archiveRuleHandler = archiveRuleHandler;
        _acknowledgeHandler = acknowledgeHandler;
        _dismissHandler = dismissHandler;
        _snoozeHandler = snoozeHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        AcknowledgeCommand = new RelayCommand<NotificationItemDto>(n => _ = AcknowledgeAsync(n));
        DismissCommand = new RelayCommand<NotificationItemDto>(n => _ = DismissAsync(n));
        SnoozeCommand = new RelayCommand<NotificationItemDto>(n => _ = SnoozeAsync(n));
        CreateRuleCommand = new AsyncRelayCommand(CreateRuleAsync);
        ArchiveRuleCommand = new RelayCommand<NotificationRuleDto>(r => _ = ArchiveRuleAsync(r));
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync, () => Notifications.Count > 0);

        SeverityOptions = new ObservableCollection<string> { "All", "Critical", "Error", "Warning", "Info" };
        AreaOptions = new ObservableCollection<string>
        {
            "All", "Billing", "Budgets", "Forecast", "Recurring", "Taxes",
            "Cash", "Debts", "Assets", "Investments", "Goals", "Retirement"
        };
        SelectedSeverity = "All";
        SelectedArea = "All";
    }

    public ICommand RefreshCommand { get; }
    public ICommand AcknowledgeCommand { get; }
    public ICommand DismissCommand { get; }
    public ICommand SnoozeCommand { get; }
    public ICommand CreateRuleCommand { get; }
    public ICommand ArchiveRuleCommand { get; }
    public ICommand ExportCsvCommand { get; }

    public ObservableCollection<NotificationItemDto> Notifications { get; } = new();
    public ObservableCollection<NotificationRuleDto> Rules { get; } = new();
    public ObservableCollection<string> SeverityOptions { get; }
    public ObservableCollection<string> AreaOptions { get; }

    private int _totalActive;
    public int TotalActive { get => _totalActive; set => SetProperty(ref _totalActive, value); }

    private int _criticalCount;
    public int CriticalCount { get => _criticalCount; set => SetProperty(ref _criticalCount, value); }

    private int _errorCount;
    public int ErrorCount { get => _errorCount; set => SetProperty(ref _errorCount, value); }

    private int _warningCount;
    public int WarningCount { get => _warningCount; set => SetProperty(ref _warningCount, value); }

    private int _overdueCount;
    public int OverdueCount { get => _overdueCount; set => SetProperty(ref _overdueCount, value); }

    private string _selectedSeverity = "All";
    public string SelectedSeverity
    {
        get => _selectedSeverity;
        set { if (SetProperty(ref _selectedSeverity, value)) ApplyFilters(); }
    }

    private string _selectedArea = "All";
    public string SelectedArea
    {
        get => _selectedArea;
        set { if (SetProperty(ref _selectedArea, value)) ApplyFilters(); }
    }

    private string? _searchText;
    public string? SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) ApplyFilters(); }
    }

    private bool _includeAcknowledged;
    public bool IncludeAcknowledged
    {
        get => _includeAcknowledged;
        set { if (SetProperty(ref _includeAcknowledged, value)) _ = LoadAsync(); }
    }

    private NotificationItemDto? _selectedNotification;
    public NotificationItemDto? SelectedNotification
    {
        get => _selectedNotification;
        set { SetProperty(ref _selectedNotification, value); OnPropertyChanged(nameof(HasSelectedNotification)); }
    }

    public bool HasSelectedNotification => SelectedNotification != null;

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private string? _errorMessage;
    public string? ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    // Rule creation fields
    private string _newRuleCode = string.Empty;
    public string NewRuleCode { get => _newRuleCode; set => SetProperty(ref _newRuleCode, value); }

    private string _newRuleArea = "Billing";
    public string NewRuleArea { get => _newRuleArea; set => SetProperty(ref _newRuleArea, value); }

    private string _newRuleSeverity = "Warning";
    public string NewRuleSeverity { get => _newRuleSeverity; set => SetProperty(ref _newRuleSeverity, value); }

    private string _newRuleConfig = "{}";
    public string NewRuleConfig { get => _newRuleConfig; set => SetProperty(ref _newRuleConfig, value); }

    private IReadOnlyList<NotificationItemDto> _allNotifications = [];

    public async Task LoadAsync()
    {
        if (_centerHandler == null) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var asOfDate = DateOnly.FromDateTime(DateTime.Today);
            var center = await _centerHandler.HandleAsync(asOfDate, IncludeAcknowledged, CancellationToken.None);

            TotalActive = center.Summary.TotalActive;
            CriticalCount = center.Summary.CriticalCount;
            ErrorCount = center.Summary.ErrorCount;
            WarningCount = center.Summary.WarningCount;
            OverdueCount = center.Summary.OverdueCount;

            _allNotifications = center.Notifications;
            ApplyFilters();

            // Also load rules
            if (_rulesHandler != null)
            {
                var rules = await _rulesHandler.HandleAsync(false, CancellationToken.None);
                Rules.Clear();
                foreach (var r in rules) Rules.Add(r);
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

    private void ApplyFilters()
    {
        IEnumerable<NotificationItemDto> filtered = _allNotifications;

        if (SelectedSeverity != "All")
            filtered = filtered.Where(n => n.Severity == SelectedSeverity);
        if (SelectedArea != "All")
            filtered = filtered.Where(n => n.Area == SelectedArea);
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            filtered = filtered.Where(n =>
                n.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                n.Body.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        Notifications.Clear();
        foreach (var n in filtered)
            Notifications.Add(n);
    }

    private async Task AcknowledgeAsync(NotificationItemDto? item)
    {
        if (item == null || _acknowledgeHandler == null) return;

        try
        {
            await _acknowledgeHandler.HandleAsync(
                new AcknowledgeNotificationCommand(item.NotificationId, "Acknowledged by user",
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Notification acknowledged");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error($"Failed: {ex.Message}");
        }
    }

    private async Task DismissAsync(NotificationItemDto? item)
    {
        if (item == null || _dismissHandler == null) return;

        try
        {
            await _dismissHandler.HandleAsync(
                new DismissNotificationCommand(item.NotificationId, "Dismissed by user",
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Notification dismissed");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error($"Failed: {ex.Message}");
        }
    }

    private async Task SnoozeAsync(NotificationItemDto? item)
    {
        if (item == null || _snoozeHandler == null) return;

        try
        {
            var snoozeUntil = DateOnly.FromDateTime(DateTime.Today).AddDays(7);
            await _snoozeHandler.HandleAsync(
                new SnoozeNotificationCommand(item.NotificationId, snoozeUntil, "Snoozed 7 days",
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Notification snoozed for 7 days");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error($"Failed: {ex.Message}");
        }
    }

    private async Task CreateRuleAsync()
    {
        if (_createRuleHandler == null || string.IsNullOrWhiteSpace(NewRuleCode)) return;

        try
        {
            await _createRuleHandler.HandleAsync(
                new CreateNotificationRuleCommand(null, NewRuleCode, NewRuleArea, NewRuleSeverity,
                    NewRuleConfig, true, DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Rule created");
            NewRuleCode = string.Empty;
            NewRuleConfig = "{}";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error($"Failed: {ex.Message}");
        }
    }

    private async Task ArchiveRuleAsync(NotificationRuleDto? rule)
    {
        if (rule == null || _archiveRuleHandler == null) return;

        try
        {
            await _archiveRuleHandler.HandleAsync(
                new ArchiveNotificationRuleCommand(rule.RuleId, "Archived by user",
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Rule archived");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error($"Failed: {ex.Message}");
        }
    }

    private async Task ExportCsvAsync()
    {
        if (_exportService == null || Notifications.Count == 0) return;

        var headers = new[] { "Severity", "Area", "Title", "Body", "Due Date", "Status" };
        var rows = Notifications.Select(n => new[]
        {
            n.Severity, n.Area, n.Title, n.Body,
            n.DueDate?.ToString("yyyy-MM-dd") ?? "", n.Status
        });

        await _exportService.ExportCsvAsync("Notifications", headers, rows);
    }
}
