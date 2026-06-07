using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections.Installments;
using DebtManager.Domain.ValueObjects;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Obligation Detail view showing installments, payments, audit trail, and actions.
/// </summary>
public sealed class ObligationDetailViewModel : ObservableObject
{
    private readonly GetFinancialSnapshotHandler _snapshotHandler;
    private readonly CloseObligationHandler _closeHandler;
    private readonly GetPaymentsLedgerHandler? _ledgerHandler;
    private readonly ReversePaymentHandler? _reverseHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly Action? _onBack;
    private readonly Action<Guid>? _onRecordPayment;
    private readonly Action<Guid>? _onDefineSchedule;
    private readonly Action? _onRefreshDashboard;
    private readonly IToastService? _toastService;

    public ObligationDetailViewModel(
        GetFinancialSnapshotHandler snapshotHandler,
        CloseObligationHandler closeHandler,
        Guid actorUserId,
        Guid deviceId,
        Action? onBack = null,
        Action<Guid>? onRecordPayment = null,
        Action<Guid>? onDefineSchedule = null,
        Action? onRefreshDashboard = null,
        IToastService? toastService = null,
        GetPaymentsLedgerHandler? ledgerHandler = null,
        ReversePaymentHandler? reverseHandler = null)
    {
        _snapshotHandler = snapshotHandler;
        _closeHandler = closeHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _onBack = onBack;
        _onRecordPayment = onRecordPayment;
        _onDefineSchedule = onDefineSchedule;
        _onRefreshDashboard = onRefreshDashboard;
        _toastService = toastService;
        _ledgerHandler = ledgerHandler;
        _reverseHandler = reverseHandler;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        BackCommand = new RelayCommand(() => _onBack?.Invoke());
        RecordPaymentCommand = new RelayCommand(() => _onRecordPayment?.Invoke(ObligationId));
        DefineScheduleCommand = new RelayCommand(() => _onDefineSchedule?.Invoke(ObligationId));
        CloseObligationCommand = new AsyncRelayCommand(CloseObligationAsync, () => !IsClosed && !IsLoading);

        // Payment commands
        RefreshPaymentsCommand = new AsyncRelayCommand(LoadPaymentsAsync);
        ReverseSelectedPaymentCommand = new AsyncRelayCommand(ReverseSelectedPaymentAsync, CanReverseSelectedPayment);
        ConfirmReversalCommand = new AsyncRelayCommand(ConfirmReversalAsync);
        CancelReversalCommand = new RelayCommand(CancelReversal);

        // Initialize collection view for filtering
        PaymentsView = CollectionViewSource.GetDefaultView(Payments);
        PaymentsView.Filter = FilterPayment;

        // Default values
        ShowReversals = true;
    }

    // Commands
    public ICommand RefreshCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand RecordPaymentCommand { get; }
    public ICommand DefineScheduleCommand { get; }
    public ICommand CloseObligationCommand { get; }

    // Payment commands
    public ICommand RefreshPaymentsCommand { get; }
    public ICommand ReverseSelectedPaymentCommand { get; }
    public ICommand ConfirmReversalCommand { get; }
    public ICommand CancelReversalCommand { get; }

    // Obligation ID to load
    private Guid _obligationId;
    public Guid ObligationId
    {
        get => _obligationId;
        set => SetProperty(ref _obligationId, value);
    }

    // Summary properties
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private string _obligationType = string.Empty;
    public string ObligationType
    {
        get => _obligationType;
        set => SetProperty(ref _obligationType, value);
    }

    private decimal _principal;
    public decimal Principal
    {
        get => _principal;
        set => SetProperty(ref _principal, value);
    }

    private decimal _totalPaid;
    public decimal TotalPaid
    {
        get => _totalPaid;
        set => SetProperty(ref _totalPaid, value);
    }

    private decimal _outstanding;
    public decimal Outstanding
    {
        get => _outstanding;
        set => SetProperty(ref _outstanding, value);
    }

    private string _currencyCode = "EGP";
    public string CurrencyCode
    {
        get => _currencyCode;
        set => SetProperty(ref _currencyCode, value);
    }

    private string _healthStatus = "Healthy";
    public string HealthStatus
    {
        get => _healthStatus;
        set => SetProperty(ref _healthStatus, value);
    }

    private bool _isClosed;
    public bool IsClosed
    {
        get => _isClosed;
        set => SetProperty(ref _isClosed, value);
    }

    private int _totalInstallments;
    public int TotalInstallments
    {
        get => _totalInstallments;
        set => SetProperty(ref _totalInstallments, value);
    }

    private int _paidInstallments;
    public int PaidInstallments
    {
        get => _paidInstallments;
        set => SetProperty(ref _paidInstallments, value);
    }

    private int _overdueInstallments;
    public int OverdueInstallments
    {
        get => _overdueInstallments;
        set => SetProperty(ref _overdueInstallments, value);
    }

    // Status
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    private string _statusMessage = "Ready";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    // Collections
    public ObservableCollection<InstallmentRowItem> Installments { get; } = new();
    public ObservableCollection<AuditRowItem> AuditEntries { get; } = new();

    // Payments collections and properties
    public ObservableCollection<PaymentRowItem> Payments { get; } = new();
    public ICollectionView PaymentsView { get; }

    private PaymentRowItem? _selectedPayment;
    public PaymentRowItem? SelectedPayment
    {
        get => _selectedPayment;
        set
        {
            if (SetProperty(ref _selectedPayment, value))
            {
                UpdateSelectedPaymentAllocations();
                OnPropertyChanged(nameof(HasSelectedPayment));
                OnPropertyChanged(nameof(CanReverseSelected));
                OnPropertyChanged(nameof(SelectedPaymentIsReversal));
                OnPropertyChanged(nameof(SelectedPaymentIsReversed));
            }
        }
    }

    public ObservableCollection<AllocationRowItem> SelectedPaymentAllocations { get; } = new();

    public bool HasSelectedPayment => SelectedPayment != null;
    public bool CanReverseSelected => SelectedPayment != null && !SelectedPayment.IsReversal && !SelectedPayment.IsReversed;
    public bool SelectedPaymentIsReversal => SelectedPayment?.IsReversal ?? false;
    public bool SelectedPaymentIsReversed => SelectedPayment?.IsReversed ?? false;

    // Payment filters
    private bool _showReversals = true;
    public bool ShowReversals
    {
        get => _showReversals;
        set
        {
            if (SetProperty(ref _showReversals, value))
            {
                PaymentsView.Refresh();
            }
        }
    }

    private string _paymentSearchText = string.Empty;
    public string PaymentSearchText
    {
        get => _paymentSearchText;
        set
        {
            if (SetProperty(ref _paymentSearchText, value))
            {
                PaymentsView.Refresh();
            }
        }
    }

    private DateTime? _paymentsFromDate;
    public DateTime? PaymentsFromDate
    {
        get => _paymentsFromDate;
        set
        {
            if (SetProperty(ref _paymentsFromDate, value))
            {
                PaymentsView.Refresh();
            }
        }
    }

    private DateTime? _paymentsToDate;
    public DateTime? PaymentsToDate
    {
        get => _paymentsToDate;
        set
        {
            if (SetProperty(ref _paymentsToDate, value))
            {
                PaymentsView.Refresh();
            }
        }
    }

    // Reversal confirmation state
    private bool _isReversalConfirmationVisible;
    public bool IsReversalConfirmationVisible
    {
        get => _isReversalConfirmationVisible;
        set => SetProperty(ref _isReversalConfirmationVisible, value);
    }

    private bool _isReversing;
    public bool IsReversing
    {
        get => _isReversing;
        set => SetProperty(ref _isReversing, value);
    }

    public bool HasPayments => Payments.Count > 0;
    public bool HasNoPayments => Payments.Count == 0;

    private void UpdateSelectedPaymentAllocations()
    {
        SelectedPaymentAllocations.Clear();
        if (SelectedPayment != null)
        {
            foreach (var alloc in SelectedPayment.Allocations)
            {
                SelectedPaymentAllocations.Add(alloc);
            }
        }
    }

    private bool FilterPayment(object obj)
    {
        if (obj is not PaymentRowItem item)
            return false;

        // Filter reversals
        if (!ShowReversals && item.IsReversal)
            return false;

        // Filter by date range
        if (PaymentsFromDate.HasValue)
        {
            var fromDateOnly = DateOnly.FromDateTime(PaymentsFromDate.Value);
            if (item.EffectiveDate < fromDateOnly)
                return false;
        }

        if (PaymentsToDate.HasValue)
        {
            var toDateOnly = DateOnly.FromDateTime(PaymentsToDate.Value);
            if (item.EffectiveDate > toDateOnly)
                return false;
        }

        // Filter by search text
        if (!string.IsNullOrWhiteSpace(PaymentSearchText))
        {
            var search = PaymentSearchText.Trim().ToLowerInvariant();
            if (!(item.Reference?.ToLowerInvariant().Contains(search) ?? false) &&
                !(item.Reason?.ToLowerInvariant().Contains(search) ?? false))
            {
                return false;
            }
        }

        return true;
    }

    private bool CanReverseSelectedPayment()
    {
        return SelectedPayment != null && !SelectedPayment.IsReversal && !SelectedPayment.IsReversed && !IsReversing;
    }

    private Task ReverseSelectedPaymentAsync()
    {
        if (!CanReverseSelectedPayment())
            return Task.CompletedTask;

        // Show inline confirmation
        IsReversalConfirmationVisible = true;
        return Task.CompletedTask;
    }

    private void CancelReversal()
    {
        IsReversalConfirmationVisible = false;
    }

    private async Task ConfirmReversalAsync()
    {
        if (SelectedPayment == null || _reverseHandler == null)
        {
            IsReversalConfirmationVisible = false;
            return;
        }

        if (SelectedPayment.IsReversal)
        {
            _toastService?.Warning("Cannot reverse a reversal");
            IsReversalConfirmationVisible = false;
            return;
        }

        if (SelectedPayment.IsReversed)
        {
            _toastService?.Warning("Payment is already reversed");
            IsReversalConfirmationVisible = false;
            return;
        }

        IsReversing = true;
        IsReversalConfirmationVisible = false;

        try
        {
            await _reverseHandler.HandleAsync(
                new ReversePaymentCommand(
                    ObligationId: ObligationId,
                    PaymentEventId: SelectedPayment.PaymentEventId,
                    EffectiveDate: DateOnly.FromDateTime(DateTime.Today),
                    Reason: "Reversed from obligation detail"
                ),
                _actorUserId,
                _deviceId,
                CancellationToken.None
            );

            _toastService?.Success($"Payment reversed: {SelectedPayment.Amount:N2} {SelectedPayment.CurrencyCode}");

            // Refresh all data
            SelectedPayment = null;
            await LoadAsync();
            _onRefreshDashboard?.Invoke();
        }
        catch (InvalidOperationException ex)
        {
            _toastService?.Warning(ex.Message);
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to reverse payment", ex);
        }
        finally
        {
            IsReversing = false;
        }
    }

    /// <summary>
    /// Load obligation details from the snapshot handler.
    /// </summary>
    public async Task LoadAsync()
    {
        if (ObligationId == Guid.Empty)
        {
            ErrorMessage = "No obligation selected.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = "Loading obligation details...";

        try
        {
            var asOfDate = DateOnly.FromDateTime(DateTime.Today);
            var snapshot = await _snapshotHandler.HandleAsync(ObligationId, asOfDate, CancellationToken.None);

            // Get obligation state
            if (snapshot.Obligations.TryGetValue(ObligationId, out var obligation))
            {
                Name = obligation.Name;
                Principal = obligation.Principal.Amount;
                TotalPaid = obligation.TotalPaid.Amount;
                Outstanding = obligation.Principal.Amount - obligation.TotalPaid.Amount;
                if (Outstanding < 0) Outstanding = 0;
                CurrencyCode = obligation.Principal.Currency.Code;
            }

            // Get installments for this obligation
            var installments = snapshot.Installments
                .Where(i => i.ObligationId == ObligationId)
                .OrderBy(i => i.DueDate)
                .ToList();

            Installments.Clear();
            foreach (var inst in installments)
            {
                Installments.Add(new InstallmentRowItem(
                    Key: inst.InstallmentKey.ToString(),
                    DueDate: inst.DueDate,
                    ExpectedAmount: inst.Expected.Amount,
                    PaidAmount: inst.Paid.Amount,
                    OutstandingAmount: inst.Outstanding.Amount,
                    Status: inst.Status.ToString(),
                    DaysOverdue: inst.DaysOverdue,
                    Risk: inst.Risk.ToString(),
                    IsFullyPaid: inst.IsFullyPaid
                ));
            }

            TotalInstallments = installments.Count;
            PaidInstallments = installments.Count(i => i.Status == InstallmentStatus.Paid);
            OverdueInstallments = installments.Count(i => i.Status == InstallmentStatus.Overdue);

            // Determine health status
            var maxDaysOverdue = installments.Any() ? installments.Max(i => i.DaysOverdue) : 0;
            HealthStatus = DetermineHealthStatus(maxDaysOverdue);

            // Get audit entries for this obligation
            var auditEntries = snapshot.Audit
                .Where(a => a.ObligationId == ObligationId || a.ObligationId == null)
                .OrderByDescending(a => a.At)
                .Take(50)
                .ToList();

            AuditEntries.Clear();
            foreach (var entry in auditEntries)
            {
                AuditEntries.Add(new AuditRowItem(
                    Timestamp: entry.At,
                    EffectiveDate: entry.EffectiveDate,
                    Category: entry.Category,
                    Message: entry.Message,
                    Severity: entry.Severity ?? "Info"
                ));
            }

            // Check if obligation is closed (look for close event in audit)
            IsClosed = auditEntries.Any(a => a.Category == "ObligationClosed");

            // Load payments
            await LoadPaymentsAsync();

            StatusMessage = $"Loaded {TotalInstallments} installments, {Payments.Count} payments, {AuditEntries.Count} audit entries.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load obligation: {ex.Message}";
            StatusMessage = "Error loading obligation.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Load payments for this obligation.
    /// </summary>
    public async Task LoadPaymentsAsync()
    {
        if (_ledgerHandler == null || ObligationId == Guid.Empty)
            return;

        try
        {
            var query = new GetPaymentsLedgerQuery(
                ObligationId: ObligationId,
                FromDate: null,
                ToDate: null
            );

            var rows = await _ledgerHandler.HandleAsync(query, CancellationToken.None);

            Payments.Clear();
            foreach (var row in rows)
            {
                Payments.Add(new PaymentRowItem(
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
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to load payments", ex);
        }
    }

    private async Task CloseObligationAsync()
    {
        if (ObligationId == Guid.Empty || IsClosed)
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var command = new CloseObligationCommand(
                ObligationId: ObligationId,
                ClosureType: ObligationClosureType.PaidInFull,
                FinalBalance: new Money(Outstanding, Currency.EGP),
                Reason: "Closed via UI",
                Notes: null
            );

            await _closeHandler.HandleAsync(command, _actorUserId, _deviceId, CancellationToken.None);

            IsClosed = true;
            StatusMessage = "Obligation closed successfully.";
            _toastService?.Success("Obligation closed");

            // Refresh to show updated state
            await LoadAsync();
            _onRefreshDashboard?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to close obligation: {ex.Message}";
            _toastService?.Error("Failed to close obligation", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string DetermineHealthStatus(int maxDaysOverdue)
    {
        if (maxDaysOverdue == 0) return "Healthy";
        if (maxDaysOverdue <= 30) return "AtRisk";
        if (maxDaysOverdue <= 90) return "Delinquent";
        return "Critical";
    }
}

/// <summary>
/// Row item for an installment in the detail view.
/// </summary>
public sealed record InstallmentRowItem(
    string Key,
    DateOnly DueDate,
    decimal ExpectedAmount,
    decimal PaidAmount,
    decimal OutstandingAmount,
    string Status,
    int DaysOverdue,
    string Risk,
    bool IsFullyPaid
)
{
    public string DueDateDisplay => DueDate.ToString("MMM dd, yyyy");
    public string StatusDisplay => Status;
}

/// <summary>
/// Row item for an audit entry in the detail view.
/// </summary>
public sealed record AuditRowItem(
    DateTimeOffset Timestamp,
    DateOnly EffectiveDate,
    string Category,
    string Message,
    string Severity
)
{
    public string TimestampDisplay => Timestamp.ToString("yyyy-MM-dd HH:mm");
    public string EffectiveDateDisplay => EffectiveDate.ToString("MMM dd, yyyy");
}

// PaymentRowItem and AllocationRowItem are defined in PaymentsListViewModel.cs
// and are reused here to avoid duplication.
