using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Security;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

/// <summary>
/// ViewModel for creating a new obligation.
/// </summary>
public sealed class CreateObligationViewModel : ObservableObject
{
    private readonly CreateObligationHandler _handler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly Action? _onSuccess;
    private readonly Action? _onCancel;
    private readonly IToastService? _toastService;

    public CreateObligationViewModel(
        CreateObligationHandler handler,
        Guid actorUserId,
        Guid deviceId,
        Action? onSuccess = null,
        Action? onCancel = null,
        IToastService? toastService = null)
    {
        _handler = handler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _onSuccess = onSuccess;
        _onCancel = onCancel;
        _toastService = toastService;

        CreateCommand = new AsyncRelayCommand(CreateAsync, CanCreate);
        CancelCommand = new RelayCommand(Cancel);

        // Default values
        StartDate = DateOnly.FromDateTime(DateTime.Today);
        CurrencyCode = "EGP";
    }

    // Commands
    public ICommand CreateCommand { get; }
    public ICommand CancelCommand { get; }

    // Properties
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
                ValidateName();
        }
    }

    private string _obligationType = "Loan";
    public string ObligationType
    {
        get => _obligationType;
        set => SetProperty(ref _obligationType, value);
    }

    private decimal _principal;
    public decimal Principal
    {
        get => _principal;
        set
        {
            if (SetProperty(ref _principal, value))
                ValidatePrincipal();
        }
    }

    private string _currencyCode = "EGP";
    public string CurrencyCode
    {
        get => _currencyCode;
        set => SetProperty(ref _currencyCode, value);
    }

    private DateOnly _startDate;
    public DateOnly StartDate
    {
        get => _startDate;
        set => SetProperty(ref _startDate, value);
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

    private string? _nameError;
    public string? NameError
    {
        get => _nameError;
        set => SetProperty(ref _nameError, value);
    }

    private string? _principalError;
    public string? PrincipalError
    {
        get => _principalError;
        set => SetProperty(ref _principalError, value);
    }

    // Available obligation types
    public ObservableCollection<string> ObligationTypes { get; } = new()
    {
        "Loan",
        "Mortgage",
        "Credit Card",
        "Education",
        "Subscription",
        "Other"
    };

    // Available currencies
    public ObservableCollection<string> Currencies { get; } = new()
    {
        "EGP",
        "USD",
        "EUR",
        "GBP",
        "SAR",
        "AED"
    };

    private void ValidateName()
    {
        var result = InputValidator.ValidateName(Name);
        NameError = result.IsValid ? null : result.ErrorMessage;
    }

    private void ValidatePrincipal()
    {
        var result = InputValidator.ValidateAmount(Principal, "Principal");
        PrincipalError = result.IsValid ? null : result.ErrorMessage;
    }

    private bool CanCreate()
    {
        return !IsLoading &&
               !string.IsNullOrWhiteSpace(Name) &&
               Principal > 0 &&
               string.IsNullOrEmpty(NameError) &&
               string.IsNullOrEmpty(PrincipalError);
    }

    private async Task CreateAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            // Validate all fields
            var validation = new ValidationBuilder()
                .ValidateName(Name)
                .ValidateAmount(Principal, "Principal")
                .ValidateCurrencyCode(CurrencyCode)
                .ValidateDate(StartDate, "Start date")
                .Build();

            if (!validation.IsValid)
            {
                ErrorMessage = validation.ErrorMessage;
                return;
            }

            var obligationId = Guid.NewGuid();

            await _handler.HandleAsync(
                new CreateObligationCommand(
                    obligationId,
                    Name,
                    ObligationType,
                    Principal,
                    CurrencyCode,
                    StartDate),
                _actorUserId,
                _deviceId,
                CancellationToken.None);

            _onSuccess?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create obligation: {ex.Message}";
            _toastService?.Error("Failed to create obligation", ex);
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
