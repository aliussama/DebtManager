using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Audit Trail view.
/// Provides a filterable, sortable audit log with export functionality.
/// </summary>
public sealed class AuditTrailViewModel : ObservableObject
{
    private readonly GetAuditTrailHandler? _auditHandler;
    private readonly GetObligationsListHandler? _obligationsHandler;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;

    public AuditTrailViewModel(
        GetAuditTrailHandler? auditHandler = null,
        GetObligationsListHandler? obligationsHandler = null,
        IToastService? toastService = null,
        IExportService? exportService = null)
    {
        _auditHandler = auditHandler;
        _obligationsHandler = obligationsHandler;
        _toastService = toastService;
        _exportService = exportService;

        // Initialize collection view for filtering
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;

        // Default sort by EffectiveDate descending
        ItemsView.SortDescriptions.Add(new SortDescription(nameof(AuditTrailRowItem.EffectiveDate), ListSortDirection.Descending));
        ItemsView.SortDescriptions.Add(new SortDescription(nameof(AuditTrailRowItem.At), ListSortDirection.Descending));

        // Initialize commands
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ExportCsvCommand = new AsyncRelayCommand(ExportToCsvAsync, CanExportCsv);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
    }

    // Commands
    public ICommand RefreshCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ClearFiltersCommand { get; }

    // Collections
    public ObservableCollection<AuditTrailRowItem> Items { get; } = new();
    public ObservableCollection<ObligationDropdownItem> Obligations { get; } = new();
    public ICollectionView ItemsView { get; }

    // Filter options
    public string[] CategoryOptions { get; } = { "All", "Payment", "Obligation", "Schedule", "Rules", "Charge", "Sync", "Security", "System" };
    public string[] SeverityOptions { get; } = { "All", "Info", "Warning", "Error" };

    // Selected item
    private AuditTrailRowItem? _selectedItem;
    public AuditTrailRowItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(HasSelectedItem));
            }
        }
    }

    public bool HasSelectedItem => SelectedItem != null;

    // Filters
    private ObligationDropdownItem? _selectedObligation;
    public ObligationDropdownItem? SelectedObligation
    {
        get => _selectedObligation;
        set
        {
            if (SetProperty(ref _selectedObligation, value))
            {
                // Reload data when obligation filter changes (server-side filter)
                _ = LoadAsync();
            }
        }
    }

    private DateTime? _fromDate;
    public DateTime? FromDate
    {
        get => _fromDate;
        set
        {
            if (SetProperty(ref _fromDate, value))
            {
                ItemsView.Refresh();
            }
        }
    }

    private DateTime? _toDate;
    public DateTime? ToDate
    {
        get => _toDate;
        set
        {
            if (SetProperty(ref _toDate, value))
            {
                ItemsView.Refresh();
            }
        }
    }

    private string _selectedCategory = "All";
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ItemsView.Refresh();
            }
        }
    }

    private string _selectedSeverity = "All";
    public string SelectedSeverity
    {
        get => _selectedSeverity;
        set
        {
            if (SetProperty(ref _selectedSeverity, value))
            {
                ItemsView.Refresh();
            }
        }
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ItemsView.Refresh();
            }
        }
    }

    // Status
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    // Count helpers
    private int _totalCount;
    public int TotalCount
    {
        get => _totalCount;
        set => SetProperty(ref _totalCount, value);
    }

    private int _filteredCount;
    public int FilteredCount
    {
        get => _filteredCount;
        set => SetProperty(ref _filteredCount, value);
    }

    // Empty state helpers
    public bool HasItems => Items.Count > 0;
    public bool HasNoItems => Items.Count == 0 && !IsLoading;

    /// <summary>
    /// Load audit trail data.
    /// </summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = "Loading audit trail...";

        try
        {
            // Load obligations for dropdown if not already loaded
            if (_obligationsHandler != null && Obligations.Count == 0)
            {
                var asOfDate = DateOnly.FromDateTime(DateTime.Today);
                var obligations = await _obligationsHandler.HandleAsync(asOfDate, "EGP", CancellationToken.None);

                Obligations.Clear();
                Obligations.Add(new ObligationDropdownItem(null, "All Obligations"));
                foreach (var o in obligations)
                {
                    Obligations.Add(new ObligationDropdownItem(o.ObligationId, o.Name));
                }

                // Set default selection
                if (SelectedObligation == null)
                {
                    SelectedObligation = Obligations.FirstOrDefault();
                }
            }

            // Load audit trail
            if (_auditHandler != null)
            {
                var query = new GetAuditTrailQuery(
                    ObligationId: SelectedObligation?.Id,
                    FromDate: null, // Load all, filter client-side for more responsive UI
                    ToDate: null
                );

                var rows = await _auditHandler.HandleAsync(query, CancellationToken.None);

                Items.Clear();
                foreach (var row in rows)
                {
                    Items.Add(new AuditTrailRowItem(
                        At: row.At,
                        EffectiveDate: row.EffectiveDate,
                        Category: row.Category,
                        Severity: row.Severity,
                        Message: row.Message,
                        RelatedEventId: row.RelatedEventId,
                        ObligationId: row.ObligationId,
                        ObligationName: row.ObligationName
                    ));
                }

                TotalCount = Items.Count;
                UpdateFilteredCount();

                OnPropertyChanged(nameof(HasItems));
                OnPropertyChanged(nameof(HasNoItems));

                StatusText = $"Loaded {Items.Count} audit entries";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            _toastService?.Error("Failed to load audit trail", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool FilterItem(object obj)
    {
        if (obj is not AuditTrailRowItem item)
            return false;

        // Filter by date range
        if (FromDate.HasValue)
        {
            var fromDateOnly = DateOnly.FromDateTime(FromDate.Value);
            if (item.EffectiveDate < fromDateOnly)
                return false;
        }

        if (ToDate.HasValue)
        {
            var toDateOnly = DateOnly.FromDateTime(ToDate.Value);
            if (item.EffectiveDate > toDateOnly)
                return false;
        }

        // Filter by category
        if (SelectedCategory != "All" && !string.Equals(item.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase))
            return false;

        // Filter by severity
        if (SelectedSeverity != "All" && !string.Equals(item.Severity, SelectedSeverity, StringComparison.OrdinalIgnoreCase))
            return false;

        // Filter by search text
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim().ToLowerInvariant();
            if (!item.Message.ToLowerInvariant().Contains(search) &&
                !(item.ObligationName?.ToLowerInvariant().Contains(search) ?? false) &&
                !item.Category.ToLowerInvariant().Contains(search))
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateFilteredCount()
    {
        var count = 0;
        foreach (var item in ItemsView)
        {
            count++;
        }
        FilteredCount = count;
    }

    private void ClearFilters()
    {
        FromDate = null;
        ToDate = null;
        SelectedCategory = "All";
        SelectedSeverity = "All";
        SearchText = string.Empty;
        UpdateFilteredCount();
    }

    private bool CanExportCsv()
    {
        return FilteredCount > 0;
    }

    private async Task ExportToCsvAsync()
    {
        if (!CanExportCsv())
        {
            _toastService?.Warning("No entries to export");
            return;
        }

        if (_exportService == null)
        {
            _toastService?.Error("Export service not available");
            return;
        }

        var headers = new List<string>
        {
            "At", "EffectiveDate", "Category", "Severity", "Message",
            "RelatedEventId", "ObligationId", "ObligationName"
        };

        var rows = ItemsView.Cast<AuditTrailRowItem>()
            .Select(item => new List<string?>
            {
                item.At.ToString("o"),
                item.EffectiveDate.ToString("yyyy-MM-dd"),
                item.Category,
                item.Severity,
                item.Message,
                item.RelatedEventId?.ToString(),
                item.ObligationId?.ToString(),
                item.ObligationName
            } as IReadOnlyList<string?>)
            .ToList();

        await _exportService.ExportCsvAsync(
            $"AuditTrail_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            headers,
            rows,
            CancellationToken.None);
    }
}

/// <summary>
/// Row item for displaying an audit entry.
/// </summary>
public sealed record AuditTrailRowItem(
    DateTimeOffset At,
    DateOnly EffectiveDate,
    string Category,
    string Severity,
    string Message,
    Guid? RelatedEventId,
    Guid? ObligationId,
    string? ObligationName
)
{
    public string AtDisplay => At.ToString("yyyy-MM-dd HH:mm:ss");
    public string EffectiveDateDisplay => EffectiveDate.ToString("MMM dd, yyyy");
    public string MessageTruncated => Message.Length > 80 ? Message[..77] + "..." : Message;
    public string ObligationDisplay => ObligationName ?? (ObligationId?.ToString()[..8] + "...") ?? "—";
    public string RelatedEventIdDisplay => RelatedEventId?.ToString()[..8] + "..." ?? "—";
    
    // Severity colors
    public bool IsInfo => string.Equals(Severity, "Info", StringComparison.OrdinalIgnoreCase);
    public bool IsWarning => string.Equals(Severity, "Warning", StringComparison.OrdinalIgnoreCase);
    public bool IsError => string.Equals(Severity, "Error", StringComparison.OrdinalIgnoreCase);
}
