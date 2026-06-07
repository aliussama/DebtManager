using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Events;
using DebtManager.Domain.Scheduling;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Security;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

/// <summary>
/// ViewModel for defining a schedule for an obligation.
/// Supports Fixed Dates, Recurring, and Amortization schedule types.
/// </summary>
public sealed class DefineScheduleViewModel : ObservableObject
{
    private readonly DefineScheduleHandler _handler;
    private readonly IEventStore _eventStore;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly Action? _onSuccess;
    private readonly Action? _onCancel;
    private readonly IToastService? _toastService;

    // Default values (used when config not available)
    private const string DefaultCurrencyValue = "EGP";
    private const string DefaultTimezoneValue = "Africa/Cairo";

    public DefineScheduleViewModel(
        DefineScheduleHandler handler,
        IEventStore eventStore,
        Guid actorUserId,
        Guid deviceId,
        Action? onSuccess = null,
        Action? onCancel = null,
        SecureConfiguration? config = null,
        IToastService? toastService = null)
    {
        _handler = handler;
        _eventStore = eventStore;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _onSuccess = onSuccess;
        _onCancel = onCancel;
        _toastService = toastService;

        DefineCommand = new AsyncRelayCommand(DefineAsync, CanDefine);
        CancelCommand = new RelayCommand(Cancel);
        LoadObligationsCommand = new AsyncRelayCommand(LoadObligationsAsync);
        AddFixedDateCommand = new RelayCommand(AddFixedDate);
        RemoveFixedDateCommand = new RelayCommand<FixedDateItemViewModel>(RemoveFixedDate);

        // Load defaults from configuration or use fallbacks
        var defaultCurrency = config?.Get(ConfigKeys.DefaultCurrency) ?? DefaultCurrencyValue;
        var defaultTimezone = config?.Get(ConfigKeys.DefaultTimeZone) ?? DefaultTimezoneValue;

        // Defaults
        EffectiveDate = DateOnly.FromDateTime(DateTime.Today);
        Timezone = defaultTimezone;
        CurrencyCode = defaultCurrency;
        RecurrenceDayOfMonth = 1;
        RecurrenceCount = 12;
        RecurrenceAmount = 0;
    }

    // Commands
    public ICommand DefineCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand LoadObligationsCommand { get; }
    public ICommand AddFixedDateCommand { get; }
    public ICommand RemoveFixedDateCommand { get; }

    // Obligations dropdown
    public ObservableCollection<ObligationOption> Obligations { get; } = new();

    private ObligationOption? _selectedObligation;
    public ObligationOption? SelectedObligation
    {
        get => _selectedObligation;
        set
        {
            if (SetProperty(ref _selectedObligation, value) && value != null)
            {
                CurrencyCode = value.CurrencyCode;
            }
        }
    }

    // Schedule type selection
    public ObservableCollection<string> ScheduleTypes { get; } = new()
    {
        "Fixed Dates",
        "Recurring (Monthly)",
        "Recurring (Quarterly)",
        "Recurring (Annual)",
        "Amortization"
    };

    private string _selectedScheduleType = "Fixed Dates";
    public string SelectedScheduleType
    {
        get => _selectedScheduleType;
        set
        {
            if (SetProperty(ref _selectedScheduleType, value))
            {
                OnPropertyChanged(nameof(IsFixedDates));
                OnPropertyChanged(nameof(IsRecurring));
                OnPropertyChanged(nameof(IsAmortization));
            }
        }
    }

    public bool IsFixedDates => SelectedScheduleType == "Fixed Dates";
    public bool IsRecurring => SelectedScheduleType.StartsWith("Recurring");
    public bool IsAmortization => SelectedScheduleType == "Amortization";

    // Common fields
    private DateOnly _effectiveDate;
    public DateOnly EffectiveDate
    {
        get => _effectiveDate;
        set => SetProperty(ref _effectiveDate, value);
    }

    private string _timezone = "Africa/Cairo";
    public string Timezone
    {
        get => _timezone;
        set => SetProperty(ref _timezone, value);
    }

    private string _currencyCode = "EGP";
    public string CurrencyCode
    {
        get => _currencyCode;
        set => SetProperty(ref _currencyCode, value);
    }

    public ObservableCollection<string> Currencies { get; } = new()
    {
        "EGP", "USD", "EUR", "GBP", "SAR", "AED"
    };

    public ObservableCollection<string> Timezones { get; } = new()
    {
        "Africa/Cairo",
        "UTC",
        "America/New_York",
        "Europe/London",
        "Asia/Dubai"
    };

    // Fixed Dates fields
    public ObservableCollection<FixedDateItemViewModel> FixedDates { get; } = new();

    private void AddFixedDate()
    {
        var nextDate = FixedDates.Count > 0
            ? FixedDates[^1].DueDate.AddMonths(1)
            : EffectiveDate.AddMonths(1);

        FixedDates.Add(new FixedDateItemViewModel
        {
            DueDate = nextDate,
            Amount = 0
        });
    }

    private void RemoveFixedDate(FixedDateItemViewModel? item)
    {
        if (item != null)
            FixedDates.Remove(item);
    }

    // Recurring fields
    private int _recurrenceDayOfMonth = 1;
    public int RecurrenceDayOfMonth
    {
        get => _recurrenceDayOfMonth;
        set => SetProperty(ref _recurrenceDayOfMonth, Math.Clamp(value, 1, 31));
    }

    private int _recurrenceCount = 12;
    public int RecurrenceCount
    {
        get => _recurrenceCount;
        set => SetProperty(ref _recurrenceCount, Math.Max(1, value));
    }

    private decimal _recurrenceAmount;
    public decimal RecurrenceAmount
    {
        get => _recurrenceAmount;
        set => SetProperty(ref _recurrenceAmount, value);
    }

    private DateOnly? _recurrenceEndDate;
    public DateOnly? RecurrenceEndDate
    {
        get => _recurrenceEndDate;
        set => SetProperty(ref _recurrenceEndDate, value);
    }

    // Amortization fields
    private decimal _amortizationPrincipal;
    public decimal AmortizationPrincipal
    {
        get => _amortizationPrincipal;
        set => SetProperty(ref _amortizationPrincipal, value);
    }

    private decimal _amortizationInterestRate;
    public decimal AmortizationInterestRate
    {
        get => _amortizationInterestRate;
        set => SetProperty(ref _amortizationInterestRate, value);
    }

    private int _amortizationTermMonths = 12;
    public int AmortizationTermMonths
    {
        get => _amortizationTermMonths;
        set => SetProperty(ref _amortizationTermMonths, Math.Max(1, value));
    }

    private DateOnly _amortizationStartDate = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly AmortizationStartDate
    {
        get => _amortizationStartDate;
        set => SetProperty(ref _amortizationStartDate, value);
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

    public async Task LoadObligationsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

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
                var created = JsonSerializer.Deserialize<ObligationCreated>(
                    envelope.PayloadJson, DomainJson.Options);

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

    private bool CanDefine()
    {
        if (IsLoading || SelectedObligation == null)
            return false;

        if (IsFixedDates)
            return FixedDates.Count > 0 && FixedDates.All(d => d.Amount > 0);

        if (IsRecurring)
            return RecurrenceAmount > 0 && RecurrenceCount > 0;

        if (IsAmortization)
            return AmortizationPrincipal > 0 && AmortizationTermMonths > 0;

        return false;
    }

    private async Task DefineAsync()
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
            var scheduleId = Guid.NewGuid();
            string scheduleType;
            string scheduleSpecJson;

            if (IsFixedDates)
            {
                scheduleType = "fixed_dates";
                var spec = new FixedDatesScheduleSpec(
                    CurrencyCode,
                    FixedDates.Select(d => new FixedDateItem(d.DueDate, d.Amount)).ToList(),
                    new List<string>()
                );
                scheduleSpecJson = JsonSerializer.Serialize(spec, DomainJson.Options);
            }
            else if (IsAmortization)
            {
                scheduleType = "amortization";
                var spec = new
                {
                    principal = AmortizationPrincipal,
                    annualInterestRate = AmortizationInterestRate,
                    termMonths = AmortizationTermMonths,
                    startDate = AmortizationStartDate.ToString("yyyy-MM-dd"),
                    currencyCode = CurrencyCode
                };
                scheduleSpecJson = JsonSerializer.Serialize(spec, DomainJson.Options);
            }
            else
            {
                // Recurring
                var pattern = SelectedScheduleType switch
                {
                    "Recurring (Monthly)" => "monthly",
                    "Recurring (Quarterly)" => "quarterly",
                    "Recurring (Annual)" => "annual",
                    _ => "monthly"
                };

                scheduleType = $"recurring_{pattern}";

                // Build a simple recurring spec as JSON
                var spec = new
                {
                    pattern,
                    dayOfMonth = RecurrenceDayOfMonth,
                    startDate = EffectiveDate.ToString("yyyy-MM-dd"),
                    endDate = RecurrenceEndDate?.ToString("yyyy-MM-dd"),
                    maxOccurrences = RecurrenceCount,
                    amount = RecurrenceAmount,
                    currencyCode = CurrencyCode
                };
                scheduleSpecJson = JsonSerializer.Serialize(spec, DomainJson.Options);
            }

            await _handler.HandleAsync(
                new DefineScheduleCommand(
                    scheduleId,
                    SelectedObligation.Id,
                    scheduleType,
                    scheduleSpecJson,
                    Timezone,
                    EffectiveDate),
                _actorUserId,
                _deviceId,
                CancellationToken.None);

            _onSuccess?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to define schedule: {ex.Message}";
            _toastService?.Error("Failed to define schedule", ex);
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
/// ViewModel for a single fixed date item in the schedule.
/// </summary>
public sealed class FixedDateItemViewModel : ObservableObject
{
    private DateOnly _dueDate;
    public DateOnly DueDate
    {
        get => _dueDate;
        set => SetProperty(ref _dueDate, value);
    }

    private decimal _amount;
    public decimal Amount
    {
        get => _amount;
        set => SetProperty(ref _amount, value);
    }
}
