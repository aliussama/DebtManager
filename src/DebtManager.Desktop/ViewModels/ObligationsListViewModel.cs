using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Obligations List view.
/// Provides full-featured list with search, filter, sort, and quick actions.
/// </summary>
public sealed class ObligationsListViewModel : ObservableObject
{
    private readonly GetObligationsListHandler? _listHandler;
    private readonly CloseObligationHandler? _closeHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly Action? _onCreateObligation;
    private readonly Action<Guid>? _onOpenDetail;
    private readonly Action<Guid>? _onRecordPayment;
    private readonly Action<Guid>? _onDefineSchedule;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;
    private readonly TaggingMixin? _tagging;

    private readonly List<ObligationListItem> _allItems = new();

    public ObligationsListViewModel(
        GetObligationsListHandler? listHandler = null,
        CloseObligationHandler? closeHandler = null,
        Guid actorUserId = default,
        Guid deviceId = default,
        Action? onCreateObligation = null,
        Action<Guid>? onOpenDetail = null,
        Action<Guid>? onRecordPayment = null,
        Action<Guid>? onDefineSchedule = null,
        IToastService? toastService = null,
        IExportService? exportService = null,
        TaggingMixin? tagging = null)
    {
        _listHandler = listHandler;
        _closeHandler = closeHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _onCreateObligation = onCreateObligation;
        _onOpenDetail = onOpenDetail;
        _onRecordPayment = onRecordPayment;
        _onDefineSchedule = onDefineSchedule;
        _toastService = toastService;
        _exportService = exportService;
        _tagging = tagging;

        // Initialize commands
        CreateObligationCommand = new RelayCommand(() => _onCreateObligation?.Invoke());
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ExportCsvCommand = new AsyncRelayCommand(ExportToCsvAsync, CanExportCsv);
        OpenSelectedCommand = new RelayCommand<ObligationListItem>(OpenDetail);
        RecordPaymentCommand = new RelayCommand<ObligationListItem>(RecordPayment);
        DefineScheduleCommand = new RelayCommand<ObligationListItem>(DefineSchedule);
        CloseObligationCommand = new RelayCommand<ObligationListItem>(item => _ = CloseObligationAsync(item));

        // Initialize collection view for filtering/sorting
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;

        // Default values
        SelectedHealthFilter = "All";
        SelectedSort = "Name";
    }

    // Commands
    public ICommand CreateObligationCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand OpenSelectedCommand { get; }
    public ICommand RecordPaymentCommand { get; }
    public ICommand DefineScheduleCommand { get; }
    public ICommand CloseObligationCommand { get; }

    // Collections
    public ObservableCollection<ObligationListItem> Items { get; } = new();
    public ICollectionView ItemsView { get; }

    // Filter options
    public string[] HealthFilterOptions { get; } = { "All", "Healthy", "AtRisk", "Delinquent", "Critical", "Closed" };
    public string[] SortOptions { get; } = { "Name", "NextDueDate", "Outstanding", "OverdueCount" };

    // Tag filter
    public ObservableCollection<string> TagSuggestions { get; } = new();
    private string _selectedTagFilter = string.Empty;
    public string SelectedTagFilter
    {
        get => _selectedTagFilter;
        set { if (SetProperty(ref _selectedTagFilter, value)) _ = ApplyTagFilterAsync(); }
    }
    private HashSet<Guid>? _tagFilteredIds;

    // Selected item
    private ObligationListItem? _selectedItem;
    public ObligationListItem? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    // Search text
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

    // Health filter
    private string _selectedHealthFilter = "All";
    public string SelectedHealthFilter
    {
        get => _selectedHealthFilter;
        set
        {
            if (SetProperty(ref _selectedHealthFilter, value))
            {
                ItemsView.Refresh();
            }
        }
    }

    // Sort
    private string _selectedSort = "Name";
    public string SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (SetProperty(ref _selectedSort, value))
            {
                ApplySort();
            }
        }
    }

    // Show closed toggle
    private bool _showClosed;
    public bool ShowClosed
    {
        get => _showClosed;
        set
        {
            if (SetProperty(ref _showClosed, value))
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

    // Empty state helpers
    public bool HasObligations => Items.Count > 0;
    public bool HasNoObligations => Items.Count == 0;

    public async Task LoadAsync()
    {
        if (_listHandler == null) return;

        IsLoading = true;
        StatusText = "Loading obligations...";

        try
        {
            var asOfDate = DateOnly.FromDateTime(DateTime.Today);
            var obligations = await _listHandler.HandleAsync(asOfDate, "EGP", CancellationToken.None);

            _allItems.Clear();
            Items.Clear();

            foreach (var dto in obligations)
            {
                var item = new ObligationListItem(
                    Id: dto.ObligationId,
                    Name: dto.Name,
                    Type: dto.ObligationType,
                    Principal: dto.Principal,
                    Paid: dto.TotalPaid,
                    Outstanding: dto.Outstanding,
                    OverdueCount: dto.OverdueCount,
                    NextDueDate: dto.NextDueDate,
                    HealthStatus: dto.HealthStatus,
                    IsClosed: dto.IsClosed,
                    CurrencyCode: dto.CurrencyCode
                );
                _allItems.Add(item);
                Items.Add(item);
            }

            ApplySort();
            OnPropertyChanged(nameof(HasObligations));
            OnPropertyChanged(nameof(HasNoObligations));

            if (_tagging != null) await _tagging.LoadSuggestionsAsync(TagSuggestions);

            StatusText = $"Loaded {Items.Count} obligation(s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            _toastService?.Error("Failed to load obligations", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool FilterItem(object obj)
    {
        if (obj is not ObligationListItem item)
            return false;

        // Filter by closed status
        if (!ShowClosed && item.IsClosed)
            return false;

        // Filter by health status
        if (SelectedHealthFilter != "All")
        {
            if (SelectedHealthFilter == "Closed")
            {
                if (!item.IsClosed)
                    return false;
            }
            else if (!string.Equals(item.HealthStatus, SelectedHealthFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Filter by tag
        if (_tagFilteredIds != null && !_tagFilteredIds.Contains(item.Id))
            return false;

        // Filter by search text
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim().ToLowerInvariant();
            if (!item.Name.ToLowerInvariant().Contains(search) &&
                !item.Type.ToLowerInvariant().Contains(search))
            {
                return false;
            }
        }

        return true;
    }

    private void ApplySort()
    {
        ItemsView.SortDescriptions.Clear();

        var direction = ListSortDirection.Ascending;
        var property = SelectedSort switch
        {
            "NextDueDate" => nameof(ObligationListItem.NextDueDate),
            "Outstanding" => nameof(ObligationListItem.Outstanding),
            "OverdueCount" => nameof(ObligationListItem.OverdueCount),
            _ => nameof(ObligationListItem.Name)
        };

        // Sort descending for Outstanding and OverdueCount (highest first)
        if (SelectedSort == "Outstanding" || SelectedSort == "OverdueCount")
        {
            direction = ListSortDirection.Descending;
        }

        ItemsView.SortDescriptions.Add(new SortDescription(property, direction));
    }

    private async Task ApplyTagFilterAsync()
    {
        if (_tagging != null && !string.IsNullOrEmpty(SelectedTagFilter))
            _tagFilteredIds = await _tagging.GetEntityIdsByTagAsync(SelectedTagFilter, "Obligation");
        else
            _tagFilteredIds = null;
        ItemsView.Refresh();
    }

    private void OpenDetail(ObligationListItem? item)
    {
        if (item != null)
        {
            _onOpenDetail?.Invoke(item.Id);
        }
    }

    private void RecordPayment(ObligationListItem? item)
    {
        if (item != null)
        {
            _onRecordPayment?.Invoke(item.Id);
        }
    }

    private void DefineSchedule(ObligationListItem? item)
    {
        if (item != null)
        {
            _onDefineSchedule?.Invoke(item.Id);
        }
    }

    private async Task CloseObligationAsync(ObligationListItem? item)
    {
        if (item == null || _closeHandler == null)
            return;

        if (item.IsClosed)
        {
            _toastService?.Warning("Obligation is already closed");
            return;
        }

        try
        {
            await _closeHandler.HandleAsync(
                new CloseObligationCommand(
                    ObligationId: item.Id,
                    ClosureType: ObligationClosureType.PaidInFull,
                    FinalBalance: Money.Zero(Currency.EGP),
                    Reason: "Closed from obligations list",
                    Notes: null
                ),
                _actorUserId,
                _deviceId,
                CancellationToken.None
            );

            _toastService?.Success($"Closed: {item.Name}");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to close obligation", ex);
        }
    }

    private bool CanExportCsv()
    {
        return GetFilteredCount() > 0;
    }

    private int GetFilteredCount()
    {
        var count = 0;
        foreach (var _ in ItemsView)
        {
            count++;
        }
        return count;
    }

    private async Task ExportToCsvAsync()
    {
        var filteredCount = GetFilteredCount();
        if (filteredCount == 0)
        {
            _toastService?.Warning("No obligations to export");
            return;
        }

        if (_exportService == null)
        {
            _toastService?.Error("Export service not available");
            return;
        }

        var headers = new List<string>
        {
            "Name", "Type", "Principal", "Currency", "Paid", "Outstanding",
            "OverdueCount", "NextDueDate", "Health", "Status"
        };

        var rows = ItemsView.Cast<ObligationListItem>()
            .Select(item => new List<string?>
            {
                item.Name,
                item.Type,
                item.Principal.ToString("F2"),
                item.CurrencyCode,
                item.Paid.ToString("F2"),
                item.Outstanding.ToString("F2"),
                item.OverdueCount.ToString(),
                item.NextDueDate?.ToString("yyyy-MM-dd"),
                item.HealthStatus,
                item.Status
            } as IReadOnlyList<string?>)
            .ToList();

        await _exportService.ExportCsvAsync(
            $"Obligations_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            headers,
            rows,
            CancellationToken.None);
    }
}

/// <summary>
/// Row item for displaying an obligation in the list.
/// </summary>
public sealed record ObligationListItem(
    Guid Id,
    string Name,
    string Type,
    decimal Principal,
    decimal Paid,
    decimal Outstanding,
    int OverdueCount,
    DateOnly? NextDueDate,
    string HealthStatus,
    bool IsClosed,
    string CurrencyCode
)
{
    public string Status => IsClosed ? "Closed" : (Outstanding <= 0 ? "Paid Off" : "Active");
    public string NextDueDateDisplay => NextDueDate?.ToString("MMM dd, yyyy") ?? "—";
}
