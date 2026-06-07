using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Events;
using DebtManager.Infrastructure.Security;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;

namespace DebtManager.Desktop.ViewModels;

/// <summary>
/// ViewModel for recording a payment with live allocation preview.
/// </summary>
public sealed class RecordPaymentViewModel : ObservableObject
{
    private readonly RecordPaymentHandler _handler;
    private readonly PreviewPaymentAllocationHandler? _previewHandler;
    private readonly IEventStore _eventStore;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly Action? _onSuccess;
    private readonly Action? _onCancel;
    private readonly IToastService? _toastService;

    // Debounce timer for preview updates
    private readonly DispatcherTimer _debounceTimer;
    private const int DebounceDelayMs = 300;

    // Default values (used when config not available)
    private const string DefaultCurrencyValue = "EGP";

    public RecordPaymentViewModel(
        RecordPaymentHandler handler,
        IEventStore eventStore,
        Guid actorUserId,
        Guid deviceId,
        Action? onSuccess = null,
        Action? onCancel = null,
        SecureConfiguration? config = null,
        IToastService? toastService = null,
        PreviewPaymentAllocationHandler? previewHandler = null)
    {
        _handler = handler;
        _eventStore = eventStore;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _onSuccess = onSuccess;
        _onCancel = onCancel;
        _toastService = toastService;
        _previewHandler = previewHandler;

        RecordCommand = new AsyncRelayCommand(RecordAsync, CanRecord);
        CancelCommand = new RelayCommand(Cancel);
        LoadObligationsCommand = new AsyncRelayCommand(LoadObligationsAsync);

        // Setup debounce timer for preview
        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(DebounceDelayMs)
        };
        _debounceTimer.Tick += async (s, e) =>
        {
            _debounceTimer.Stop();
            await UpdatePreviewAsync();
        };

        // Load defaults from configuration or use fallbacks
        var defaultCurrency = config?.Get(ConfigKeys.DefaultCurrency) ?? DefaultCurrencyValue;

        // Default values
        PaymentDate = DateOnly.FromDateTime(DateTime.Today);
        CurrencyCode = defaultCurrency;
    }

    // Commands
    public ICommand RecordCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand LoadObligationsCommand { get; }

    // Available obligations
    public ObservableCollection<ObligationOption> Obligations { get; } = new();

    // Preview collections
    public ObservableCollection<InstallmentAllocationPreviewItem> PreviewInstallments { get; } = new();
    public ObservableCollection<ChargeAllocationPreviewItem> PreviewCharges { get; } = new();

    private ObligationOption? _selectedObligation;
    public ObligationOption? SelectedObligation
    {
        get => _selectedObligation;
        set
        {
            if (SetProperty(ref _selectedObligation, value) && value != null)
            {
                CurrencyCode = value.CurrencyCode;
                TriggerPreviewUpdate();
            }
        }
    }

    private decimal _amount;
    public decimal Amount
    {
        get => _amount;
        set
        {
            if (SetProperty(ref _amount, value))
            {
                ValidateAmount();
                TriggerPreviewUpdate();
            }
        }
    }

    private string _currencyCode = "EGP";
    public string CurrencyCode
    {
        get => _currencyCode;
        set
        {
            if (SetProperty(ref _currencyCode, value))
            {
                TriggerPreviewUpdate();
            }
        }
    }

    private DateOnly _paymentDate;
    public DateOnly PaymentDate
    {
        get => _paymentDate;
        set
        {
            if (SetProperty(ref _paymentDate, value))
            {
                TriggerPreviewUpdate();
            }
        }
    }

    private string _reference = string.Empty;
    public string Reference
    {
        get => _reference;
        set => SetProperty(ref _reference, value);
    }

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

    private string? _amountError;
    public string? AmountError
    {
        get => _amountError;
        set => SetProperty(ref _amountError, value);
    }

    // Preview properties
    private bool _isPreviewLoading;
    public bool IsPreviewLoading
    {
        get => _isPreviewLoading;
        set => SetProperty(ref _isPreviewLoading, value);
    }

    private string? _previewError;
    public string? PreviewError
    {
        get => _previewError;
        set
        {
            if (SetProperty(ref _previewError, value))
            {
                OnPropertyChanged(nameof(HasPreviewError));
            }
        }
    }

    public bool HasPreviewError => !string.IsNullOrEmpty(PreviewError);

    private decimal _previewUnappliedAmount;
    public decimal PreviewUnappliedAmount
    {
        get => _previewUnappliedAmount;
        set => SetProperty(ref _previewUnappliedAmount, value);
    }

    private bool _hasSchedule = true;
    public bool HasSchedule
    {
        get => _hasSchedule;
        set
        {
            if (SetProperty(ref _hasSchedule, value))
            {
                OnPropertyChanged(nameof(ShowNoScheduleMessage));
            }
        }
    }

    public bool ShowNoScheduleMessage => !HasSchedule && SelectedObligation != null;

    public bool HasPreviewData => PreviewInstallments.Count > 0 || PreviewCharges.Count > 0 || PreviewUnappliedAmount > 0;

    public ObservableCollection<string> Currencies { get; } = new()
    {
        "EGP", "USD", "EUR", "GBP", "SAR", "AED"
    };

    private void ValidateAmount()
    {
        var result = InputValidator.ValidateAmount(Amount);
        AmountError = result.IsValid ? null : result.ErrorMessage;
    }

    private bool CanRecord()
    {
        return !IsLoading &&
               SelectedObligation != null &&
               Amount > 0 &&
               string.IsNullOrEmpty(AmountError);
    }

    private void TriggerPreviewUpdate()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async Task UpdatePreviewAsync()
    {
        if (_previewHandler == null || SelectedObligation == null || Amount <= 0)
        {
            PreviewInstallments.Clear();
            PreviewCharges.Clear();
            PreviewUnappliedAmount = 0;
            PreviewError = null;
            HasSchedule = true;
            OnPropertyChanged(nameof(HasPreviewData));
            return;
        }

        IsPreviewLoading = true;
        PreviewError = null;

        try
        {
            var result = await _previewHandler.HandleAsync(
                new PreviewPaymentAllocationCommand(
                    ObligationId: SelectedObligation.Id,
                    Amount: Amount,
                    CurrencyCode: CurrencyCode,
                    EffectiveDate: PaymentDate,
                    AsOfDate: DateOnly.FromDateTime(DateTime.Today)
                ),
                CancellationToken.None
            );

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                PreviewError = result.ErrorMessage;
                PreviewInstallments.Clear();
                PreviewCharges.Clear();
                PreviewUnappliedAmount = 0;
                HasSchedule = result.HasSchedule;
            }
            else
            {
                HasSchedule = result.HasSchedule;

                // Update installment previews
                PreviewInstallments.Clear();
                foreach (var inst in result.InstallmentAllocations)
                {
                    PreviewInstallments.Add(new InstallmentAllocationPreviewItem(
                        InstallmentKey: inst.InstallmentKey,
                        DueDate: inst.DueDate,
                        DueDateDisplay: inst.DueDate.ToString("MMM dd, yyyy"),
                        InstallmentAmount: inst.InstallmentAmount,
                        AlreadyPaid: inst.AlreadyPaid,
                        OutstandingBefore: inst.OutstandingBefore,
                        AllocatedNow: inst.AllocatedNow,
                        OutstandingAfter: inst.OutstandingAfter,
                        Status: inst.Status,
                        WillReceiveAllocation: inst.AllocatedNow > 0
                    ));
                }

                // Update charge previews
                PreviewCharges.Clear();
                foreach (var charge in result.ChargeAllocations)
                {
                    PreviewCharges.Add(new ChargeAllocationPreviewItem(
                        ChargeId: charge.ChargeId,
                        ChargeType: charge.ChargeType,
                        OutstandingBefore: charge.OutstandingBefore,
                        AllocatedNow: charge.AllocatedNow,
                        OutstandingAfter: charge.OutstandingAfter
                    ));
                }

                PreviewUnappliedAmount = result.UnappliedAmount;
            }

            OnPropertyChanged(nameof(HasPreviewData));
        }
        catch (Exception ex)
        {
            PreviewError = $"Preview failed: {ex.Message}";
            PreviewInstallments.Clear();
            PreviewCharges.Clear();
            PreviewUnappliedAmount = 0;
        }
        finally
        {
            IsPreviewLoading = false;
        }
    }

    public async Task LoadObligationsAsync()
    {
        IsLoading = true;

        try
        {
            Obligations.Clear();

            var allEvents = await _eventStore.ReadAllAsync(
                new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
                CancellationToken.None);

            var obligationCreatedEvents = allEvents
                .Where(e => e.EventType == nameof(ObligationCreated))
                .ToList();

            foreach (var envelope in obligationCreatedEvents)
            {
                var created = System.Text.Json.JsonSerializer.Deserialize<ObligationCreated>(
                    envelope.PayloadJson, Domain.ValueObjects.DomainJson.Options);

                if (created == null) continue;

                // Check if closed
                var obligationEvents = await _eventStore.ReadStreamAsync(
                    new StreamId(created.ObligationId),
                    upTo: null,
                    CancellationToken.None);

                var isClosed = obligationEvents.Any(e => e.EventType == nameof(ObligationClosed));

                if (!isClosed)
                {
                    Obligations.Add(new ObligationOption(
                        created.ObligationId,
                        created.Name,
                        created.CurrencyCode,
                        created.Principal.Amount
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load obligations: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RecordAsync()
    {
        if (SelectedObligation == null)
        {
            ErrorMessage = "Please select an obligation.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var validation = new ValidationBuilder()
                .ValidateAmount(Amount)
                .ValidateDate(PaymentDate, "Payment date")
                .Build();

            if (!validation.IsValid)
            {
                ErrorMessage = validation.ErrorMessage;
                return;
            }

            await _handler.HandleAsync(
                new RecordPaymentCommand(
                    SelectedObligation.Id,
                    Amount,
                    CurrencyCode,
                    PaymentDate,
                    Reference),
                _actorUserId,
                _deviceId,
                CancellationToken.None);

            _onSuccess?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to record payment: {ex.Message}";
            _toastService?.Error("Failed to record payment", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Cancel()
    {
        _onCancel?.Invoke();
    }
}

/// <summary>
/// Option for selecting an obligation.
/// </summary>
public sealed record ObligationOption(
    Guid Id,
    string Name,
    string CurrencyCode,
    decimal Principal
)
{
    public override string ToString() => $"{Name} ({Principal:N2} {CurrencyCode})";
}

/// <summary>
/// Preview item for installment allocation display.
/// </summary>
public sealed record InstallmentAllocationPreviewItem(
    Guid InstallmentKey,
    DateOnly DueDate,
    string DueDateDisplay,
    decimal InstallmentAmount,
    decimal AlreadyPaid,
    decimal OutstandingBefore,
    decimal AllocatedNow,
    decimal OutstandingAfter,
    string Status,
    bool WillReceiveAllocation
);

/// <summary>
/// Preview item for charge allocation display.
/// </summary>
public sealed record ChargeAllocationPreviewItem(
    Guid ChargeId,
    string ChargeType,
    decimal OutstandingBefore,
    decimal AllocatedNow,
    decimal OutstandingAfter
);
