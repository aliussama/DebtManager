using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Payments List view.
/// Provides full ledger with search, filter, reversal, and details.
/// </summary>
public sealed class PaymentsListViewModel : ObservableObject
{
    private readonly GetPaymentsLedgerHandler? _ledgerHandler;
    private readonly ReversePaymentHandler? _reverseHandler;
    private readonly GetObligationsListHandler? _obligationsHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly Action? _onRecordPayment;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;

    public PaymentsListViewModel(
        GetPaymentsLedgerHandler? ledgerHandler = null,
        ReversePaymentHandler? reverseHandler = null,
        GetObligationsListHandler? obligationsHandler = null,
        Guid actorUserId = default,
        Guid deviceId = default,
        Action? onRecordPayment = null,
        IToastService? toastService = null,
        IExportService? exportService = null)
    {
        _ledgerHandler = ledgerHandler;
        _reverseHandler = reverseHandler;
        _obligationsHandler = obligationsHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _onRecordPayment = onRecordPayment;
        _toastService = toastService;
        _exportService = exportService;

        // Initialize commands
        RecordPaymentCommand = new RelayCommand(() => _onRecordPayment?.Invoke());
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ExportCsvCommand = new AsyncRelayCommand(ExportToCsvAsync, CanExportCsv);
        ReverseSelectedPaymentCommand = new RelayCommand<PaymentRowItem>(item => _ = ReversePaymentAsync(item));

        // Initialize collection view for filtering
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;

        // Default values
        ShowReversals = true;
    }

    // Commands
    public ICommand RecordPaymentCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ReverseSelectedPaymentCommand { get; }

    // Collections
    public ObservableCollection<PaymentRowItem> Items { get; } = new();
    public ObservableCollection<ObligationDropdownItem> Obligations { get; } = new();
    public ICollectionView ItemsView { get; }

    // Selected item
    private PaymentRowItem? _selectedPayment;
    public PaymentRowItem? SelectedPayment
    {
        get => _selectedPayment;
        set
        {
            if (SetProperty(ref _selectedPayment, value))
            {
                OnPropertyChanged(nameof(HasSelectedPayment));
                OnPropertyChanged(nameof(CanReverseSelected));
            }
        }
    }

    public bool HasSelectedPayment => SelectedPayment != null;
    public bool CanReverseSelected => SelectedPayment != null && !SelectedPayment.IsReversal && !SelectedPayment.IsReversed;

    // Filters
    private Guid? _selectedObligationId;
    public Guid? SelectedObligationId
    {
        get => _selectedObligationId;
        set
        {
            if (SetProperty(ref _selectedObligationId, value))
            {
                ItemsView.Refresh();
            }
        }
    }

    private ObligationDropdownItem? _selectedObligation;
    public ObligationDropdownItem? SelectedObligation
    {
        get => _selectedObligation;
        set
        {
            if (SetProperty(ref _selectedObligation, value))
            {
                SelectedObligationId = value?.Id;
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

    private bool _showReversals = true;
    public bool ShowReversals
    {
        get => _showReversals;
        set
        {
            if (SetProperty(ref _showReversals, value))
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
    public bool HasPayments => Items.Count > 0;
    public bool HasNoPayments => Items.Count == 0;

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = "Loading payments...";

        try
        {
            // Load obligations for dropdown
            if (_obligationsHandler != null)
            {
                var asOfDate = DateOnly.FromDateTime(DateTime.Today);
                var obligations = await _obligationsHandler.HandleAsync(asOfDate, "EGP", CancellationToken.None);

                Obligations.Clear();
                Obligations.Add(new ObligationDropdownItem(null, "All Obligations"));
                foreach (var o in obligations)
                {
                    Obligations.Add(new ObligationDropdownItem(o.ObligationId, o.Name));
                }
            }

            // Load payments
            if (_ledgerHandler != null)
            {
                var query = new GetPaymentsLedgerQuery(
                    ObligationId: null, // Load all, filter client-side
                    FromDate: null,
                    ToDate: null
                );

                var rows = await _ledgerHandler.HandleAsync(query, CancellationToken.None);

                Items.Clear();
                foreach (var row in rows)
                {
                    Items.Add(new PaymentRowItem(
                        PaymentEventId: row.PaymentEventId,
                        ObligationId: row.ObligationId,
                        ObligationName: row.ObligationName,
                        EffectiveDate: row.EffectiveDate,
                        Amount: row.Amount,
                        CurrencyCode: row.CurrencyCode,
                        Reference: row.Reference,
                        IsReversal: row.IsReversal,
                        OriginalPaymentEventId: row.OriginalPaymentEventId,
                        Reason: row.Reason,
                        Allocations: row.Allocations
                            .Select(a => new AllocationRowItem(a.InstallmentKey, a.Amount, a.CurrencyCode))
                            .ToList(),
                        IsReversed: row.IsReversed
                    ));
                }

                OnPropertyChanged(nameof(HasPayments));
                OnPropertyChanged(nameof(HasNoPayments));

                StatusText = $"Loaded {Items.Count} payment(s)";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            _toastService?.Error("Failed to load payments", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool FilterItem(object obj)
    {
        if (obj is not PaymentRowItem item)
            return false;

        // Filter by obligation
        if (SelectedObligationId.HasValue && item.ObligationId != SelectedObligationId.Value)
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

        // Filter reversals
        if (!ShowReversals && item.IsReversal)
            return false;

        // Filter by search text
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim().ToLowerInvariant();
            if (!(item.Reference?.ToLowerInvariant().Contains(search) ?? false) &&
                !item.ObligationName.ToLowerInvariant().Contains(search) &&
                !(item.Reason?.ToLowerInvariant().Contains(search) ?? false))
            {
                return false;
            }
        }

        return true;
    }

    private async Task ReversePaymentAsync(PaymentRowItem? item)
    {
        if (item == null || _reverseHandler == null)
            return;

        if (item.IsReversal)
        {
            _toastService?.Warning("Cannot reverse a reversal");
            return;
        }

        if (item.IsReversed)
        {
            _toastService?.Warning("Payment is already reversed");
            return;
        }

        try
        {
            await _reverseHandler.HandleAsync(
                new ReversePaymentCommand(
                    ObligationId: item.ObligationId,
                    PaymentEventId: item.PaymentEventId,
                    EffectiveDate: DateOnly.FromDateTime(DateTime.Today),
                    Reason: "Reversed from payments list"
                ),
                _actorUserId,
                _deviceId,
                CancellationToken.None
            );

            _toastService?.Success($"Payment reversed: {item.Amount:N2} {item.CurrencyCode}");
            await LoadAsync();
        }
        catch (InvalidOperationException ex)
        {
            _toastService?.Warning(ex.Message);
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to reverse payment", ex);
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
            _toastService?.Warning("No payments to export");
            return;
        }

        if (_exportService == null)
        {
            _toastService?.Error("Export service not available");
            return;
        }

        var headers = new List<string>
        {
            "EffectiveDate", "ObligationName", "Amount", "Currency", "Reference",
            "Type", "Status", "PaymentEventId", "OriginalPaymentEventId"
        };

        var rows = ItemsView.Cast<PaymentRowItem>()
            .Select(item => new List<string?>
            {
                item.EffectiveDate.ToString("yyyy-MM-dd"),
                item.ObligationName,
                item.Amount.ToString("F2"),
                item.CurrencyCode,
                item.Reference,
                item.TypeDisplay,
                item.StatusDisplay,
                item.PaymentEventId.ToString(),
                item.OriginalPaymentEventId?.ToString()
            } as IReadOnlyList<string?>)
            .ToList();

        await _exportService.ExportCsvAsync(
            $"Payments_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            headers,
            rows,
            CancellationToken.None);
    }
}

/// <summary>
/// Row item for displaying a payment in the ledger.
/// </summary>
public sealed record PaymentRowItem(
    Guid PaymentEventId,
    Guid ObligationId,
    string ObligationName,
    DateOnly EffectiveDate,
    decimal Amount,
    string CurrencyCode,
    string? Reference,
    bool IsReversal,
    Guid? OriginalPaymentEventId,
    string? Reason,
    IReadOnlyList<AllocationRowItem> Allocations,
    bool IsReversed
)
{
    public string TypeDisplay => IsReversal ? "Reversal" : "Payment";
    public string EffectiveDateDisplay => EffectiveDate.ToString("MMM dd, yyyy");
    public string StatusDisplay => IsReversed ? "Reversed" : (IsReversal ? "Reversal" : "Active");
    public bool HasAllocations => Allocations.Count > 0;
}

/// <summary>
/// Allocation row for payment detail view.
/// </summary>
public sealed record AllocationRowItem(
    Guid InstallmentKey,
    decimal Amount,
    string CurrencyCode
);

/// <summary>
/// Dropdown item for obligation filter.
/// </summary>
public sealed record ObligationDropdownItem(
    Guid? Id,
    string Name
)
{
    public override string ToString() => Name;
}
