using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Fx;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class CurrencySettingsViewModel : ObservableObject
{
    private readonly GetCurrencySettingsHandler? _getHandler;
    private readonly SetReportingCurrencyHandler? _setCurrencyHandler;
    private readonly SetFxPolicyHandler? _setPolicyHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;

    public CurrencySettingsViewModel(
        GetCurrencySettingsHandler? getHandler = null,
        SetReportingCurrencyHandler? setCurrencyHandler = null,
        SetFxPolicyHandler? setPolicyHandler = null,
        Guid actorUserId = default,
        Guid deviceId = default,
        IToastService? toastService = null)
    {
        _getHandler = getHandler;
        _setCurrencyHandler = setCurrencyHandler;
        _setPolicyHandler = setPolicyHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;

        SaveCurrencyCommand = new AsyncRelayCommand(SaveCurrencyAsync);
        SavePolicyCommand = new AsyncRelayCommand(SavePolicyAsync);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);

        AvailableCurrencies = new ObservableCollection<string>
        {
            "EGP", "USD", "EUR", "GBP", "SAR", "AED", "JPY", "CHF", "CAD", "AUD"
        };

        AvailablePolicies = new ObservableCollection<string>(
            Enum.GetNames<FxValuationPolicy>());
    }

    public ObservableCollection<string> AvailableCurrencies { get; }
    public ObservableCollection<string> AvailablePolicies { get; }

    public ICommand SaveCurrencyCommand { get; }
    public ICommand SavePolicyCommand { get; }
    public ICommand RefreshCommand { get; }

    private string _reportingCurrencyCode = "EGP";
    public string ReportingCurrencyCode
    {
        get => _reportingCurrencyCode;
        set => SetProperty(ref _reportingCurrencyCode, value);
    }

    private string _selectedPolicy = nameof(FxValuationPolicy.NearestBefore);
    public string SelectedPolicy
    {
        get => _selectedPolicy;
        set => SetProperty(ref _selectedPolicy, value);
    }

    private int _maxAgeDays = 14;
    public int MaxAgeDays
    {
        get => _maxAgeDays;
        set => SetProperty(ref _maxAgeDays, value);
    }

    private bool _isConfigured;
    public bool IsConfigured
    {
        get => _isConfigured;
        set => SetProperty(ref _isConfigured, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public async Task LoadAsync()
    {
        if (_getHandler == null) return;

        try
        {
            IsBusy = true;
            var dto = await _getHandler.HandleAsync(CancellationToken.None);
            ReportingCurrencyCode = dto.ReportingCurrencyCode;
            SelectedPolicy = dto.Policy.ToString();
            MaxAgeDays = dto.MaxAgeDays;
            IsConfigured = dto.IsConfigured;
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveCurrencyAsync()
    {
        if (_setCurrencyHandler == null) return;

        try
        {
            IsBusy = true;
            await _setCurrencyHandler.HandleAsync(
                new SetReportingCurrencyCommand(ReportingCurrencyCode),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Reporting currency saved");
            StatusMessage = "Currency saved";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
            _toastService?.Error($"Failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SavePolicyAsync()
    {
        if (_setPolicyHandler == null) return;

        try
        {
            IsBusy = true;
            var policy = Enum.Parse<FxValuationPolicy>(SelectedPolicy);
            await _setPolicyHandler.HandleAsync(
                new SetFxPolicyCommand(policy, MaxAgeDays),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("FX policy saved");
            StatusMessage = "Policy saved";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
            _toastService?.Error($"Failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
