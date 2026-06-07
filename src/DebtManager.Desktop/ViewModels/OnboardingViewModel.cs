using DebtManager.Desktop.Services;
using DebtManager.Infrastructure.Security;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

/// <summary>
/// ViewModel for the first-run onboarding wizard.
/// A step-based wizard that collects device identity, default preferences,
/// and optional cloud sync configuration.
/// </summary>
public sealed class OnboardingViewModel : ObservableObject
{
    private readonly SecureConfiguration _config;
    private readonly IToastService? _toastService;
    private readonly Guid _deviceId;
    private readonly Action _onCompleted;

    public const int StepCount = 3;

    public OnboardingViewModel(
        SecureConfiguration config,
        Guid deviceId,
        Action onCompleted,
        IToastService? toastService = null)
    {
        _config = config;
        _deviceId = deviceId;
        _onCompleted = onCompleted;
        _toastService = toastService;

        DeviceIdDisplay = _deviceId.ToString();

        NextCommand = new RelayCommand(Next, () => CanNext);
        BackCommand = new RelayCommand(Back, () => CanBack);
        SkipCommand = new RelayCommand(Skip);
        FinishCommand = new RelayCommand(Finish, () => CanFinish);

        InitializeOptions();
        UpdateStepState();
    }

    // Commands
    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SkipCommand { get; }
    public ICommand FinishCommand { get; }

    // Step tracking
    private int _currentStepIndex;
    public int CurrentStepIndex
    {
        get => _currentStepIndex;
        private set
        {
            if (SetProperty(ref _currentStepIndex, value))
                UpdateStepState();
        }
    }

    public int TotalSteps => StepCount;

    private string _stepTitle = string.Empty;
    public string StepTitle
    {
        get => _stepTitle;
        private set => SetProperty(ref _stepTitle, value);
    }

    private string _stepDescription = string.Empty;
    public string StepDescription
    {
        get => _stepDescription;
        private set => SetProperty(ref _stepDescription, value);
    }

    public bool CanNext => CurrentStepIndex < StepCount - 1 && !HasCurrentStepErrors;
    public bool CanBack => CurrentStepIndex > 0;
    public bool CanFinish => CurrentStepIndex == StepCount - 1 && !HasCurrentStepErrors;
    public bool IsLastStep => CurrentStepIndex == StepCount - 1;
    public bool IsFirstStep => CurrentStepIndex == 0;

    // Device info (Step 1)
    public string DeviceIdDisplay { get; }

    // Defaults (Step 2)
    private string _defaultCurrencyCode = "EGP";
    public string DefaultCurrencyCode
    {
        get => _defaultCurrencyCode;
        set
        {
            if (SetProperty(ref _defaultCurrencyCode, value))
            {
                ValidateCurrency();
                NotifyNavigationChanged();
            }
        }
    }

    private string _defaultTimeZone = "Africa/Cairo";
    public string DefaultTimeZone
    {
        get => _defaultTimeZone;
        set
        {
            if (SetProperty(ref _defaultTimeZone, value))
            {
                ValidateTimeZone();
                NotifyNavigationChanged();
            }
        }
    }

    private bool _requireAppUnlock;
    public bool RequireAppUnlock
    {
        get => _requireAppUnlock;
        set => SetProperty(ref _requireAppUnlock, value);
    }

    // Sync config (Step 3)
    private string _syncBaseUrl = string.Empty;
    public string SyncBaseUrl
    {
        get => _syncBaseUrl;
        set
        {
            if (SetProperty(ref _syncBaseUrl, value))
            {
                ValidateSyncConfig();
                NotifyNavigationChanged();
            }
        }
    }

    private string _syncApiKey = string.Empty;
    public string SyncApiKey
    {
        get => _syncApiKey;
        set
        {
            if (SetProperty(ref _syncApiKey, value))
            {
                ValidateSyncConfig();
                NotifyNavigationChanged();
            }
        }
    }

    private string _syncVaultId = string.Empty;
    public string SyncVaultId
    {
        get => _syncVaultId;
        set
        {
            if (SetProperty(ref _syncVaultId, value))
            {
                ValidateSyncConfig();
                NotifyNavigationChanged();
            }
        }
    }

    // Validation errors
    private string? _currencyError;
    public string? CurrencyError
    {
        get => _currencyError;
        private set => SetProperty(ref _currencyError, value);
    }

    private string? _timeZoneError;
    public string? TimeZoneError
    {
        get => _timeZoneError;
        private set => SetProperty(ref _timeZoneError, value);
    }

    private string? _syncBaseUrlError;
    public string? SyncBaseUrlError
    {
        get => _syncBaseUrlError;
        private set => SetProperty(ref _syncBaseUrlError, value);
    }

    private string? _syncApiKeyError;
    public string? SyncApiKeyError
    {
        get => _syncApiKeyError;
        private set => SetProperty(ref _syncApiKeyError, value);
    }

    private string? _syncVaultIdError;
    public string? SyncVaultIdError
    {
        get => _syncVaultIdError;
        private set => SetProperty(ref _syncVaultIdError, value);
    }

    private string _overallStatusText = string.Empty;
    public string OverallStatusText
    {
        get => _overallStatusText;
        private set => SetProperty(ref _overallStatusText, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    // Option lists
    public ObservableCollection<string> AvailableCurrencies { get; } = new();
    public ObservableCollection<string> AvailableTimezones { get; } = new();

    public bool HasCurrentStepErrors
    {
        get
        {
            return CurrentStepIndex switch
            {
                0 => false,
                1 => !string.IsNullOrEmpty(CurrencyError) || !string.IsNullOrEmpty(TimeZoneError),
                2 => !string.IsNullOrEmpty(SyncBaseUrlError) || !string.IsNullOrEmpty(SyncApiKeyError) || !string.IsNullOrEmpty(SyncVaultIdError),
                _ => false
            };
        }
    }

    private void InitializeOptions()
    {
        AvailableCurrencies.Add("EGP");
        AvailableCurrencies.Add("USD");
        AvailableCurrencies.Add("EUR");
        AvailableCurrencies.Add("GBP");
        AvailableCurrencies.Add("SAR");
        AvailableCurrencies.Add("AED");
        AvailableCurrencies.Add("KWD");
        AvailableCurrencies.Add("QAR");

        AvailableTimezones.Add("Africa/Cairo");
        AvailableTimezones.Add("UTC");
        AvailableTimezones.Add("America/New_York");
        AvailableTimezones.Add("America/Los_Angeles");
        AvailableTimezones.Add("Europe/London");
        AvailableTimezones.Add("Europe/Paris");
        AvailableTimezones.Add("Asia/Dubai");
        AvailableTimezones.Add("Asia/Riyadh");
        AvailableTimezones.Add("Asia/Tokyo");
    }

    private void UpdateStepState()
    {
        (StepTitle, StepDescription) = CurrentStepIndex switch
        {
            0 => ("Welcome to DebtManager", "Your device has been identified and is ready. Review your device identity below."),
            1 => ("Default Preferences", "Set your default currency and timezone. These will be used when creating new obligations and schedules."),
            2 => ("Cloud Sync (Optional)", "Connect to a sync server to keep your data synchronized across devices. You can skip this and configure it later in Settings."),
            _ => ("", "")
        };

        OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(CanBack));
        OnPropertyChanged(nameof(CanFinish));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(IsFirstStep));
        OnPropertyChanged(nameof(HasCurrentStepErrors));
    }

    private void NotifyNavigationChanged()
    {
        OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(CanFinish));
        OnPropertyChanged(nameof(HasCurrentStepErrors));
    }

    private void Next()
    {
        if (CurrentStepIndex < StepCount - 1)
        {
            // Validate current step before advancing
            if (CurrentStepIndex == 1)
            {
                ValidateCurrency();
                ValidateTimeZone();
                if (HasCurrentStepErrors) return;
            }

            CurrentStepIndex++;
        }
    }

    private void Back()
    {
        if (CurrentStepIndex > 0)
            CurrentStepIndex--;
    }

    /// <summary>
    /// Skip saves minimal defaults and completes onboarding.
    /// </summary>
    private void Skip()
    {
        try
        {
            IsBusy = true;
            OverallStatusText = "Saving defaults...";

            _config.Set(ConfigKeys.DefaultCurrency, DefaultCurrencyCode.ToUpperInvariant());
            _config.Set(ConfigKeys.DefaultTimeZone, DefaultTimeZone);
            _config.Set(ConfigKeys.RequireAppUnlock, RequireAppUnlock.ToString());
            _config.Set(ConfigKeys.HasCompletedOnboarding, bool.TrueString);

            _toastService?.Success("Setup complete!");
            _onCompleted();
        }
        catch (Exception ex)
        {
            OverallStatusText = $"Failed to save: {ex.Message}";
            _toastService?.Error("Failed to save settings");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Finish validates all fields and saves everything.
    /// </summary>
    private void Finish()
    {
        ValidateCurrency();
        ValidateTimeZone();
        ValidateSyncConfig();

        if (!string.IsNullOrEmpty(CurrencyError) ||
            !string.IsNullOrEmpty(TimeZoneError) ||
            !string.IsNullOrEmpty(SyncBaseUrlError) ||
            !string.IsNullOrEmpty(SyncApiKeyError) ||
            !string.IsNullOrEmpty(SyncVaultIdError))
        {
            OverallStatusText = "Please fix validation errors before finishing.";
            return;
        }

        try
        {
            IsBusy = true;
            OverallStatusText = "Saving configuration...";

            // Save defaults
            _config.Set(ConfigKeys.DefaultCurrency, DefaultCurrencyCode.ToUpperInvariant());
            _config.Set(ConfigKeys.DefaultTimeZone, DefaultTimeZone);
            _config.Set(ConfigKeys.RequireAppUnlock, RequireAppUnlock.ToString());

            // Save sync config
            if (!string.IsNullOrWhiteSpace(SyncBaseUrl))
            {
                _config.Set(ConfigKeys.SyncBaseUrl, SyncBaseUrl.Trim());
                _config.Set(ConfigKeys.SyncApiKey, SyncApiKey.Trim());
                if (!string.IsNullOrWhiteSpace(SyncVaultId))
                    _config.Set(ConfigKeys.SyncVaultId, SyncVaultId.Trim());
                else
                    _config.Remove(ConfigKeys.SyncVaultId);
            }
            else
            {
                _config.Remove(ConfigKeys.SyncBaseUrl);
                _config.Remove(ConfigKeys.SyncApiKey);
                _config.Remove(ConfigKeys.SyncVaultId);
            }

            _config.Set(ConfigKeys.HasCompletedOnboarding, bool.TrueString);

            _toastService?.Success("Setup complete!");
            _onCompleted();
        }
        catch (Exception ex)
        {
            OverallStatusText = $"Failed to save: {ex.Message}";
            _toastService?.Error("Failed to save settings");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Validation
    private void ValidateCurrency()
    {
        if (string.IsNullOrWhiteSpace(DefaultCurrencyCode))
            CurrencyError = "Currency code is required";
        else if (!Regex.IsMatch(DefaultCurrencyCode, @"^[A-Z]{3}$"))
            CurrencyError = "Must be 3 uppercase letters (e.g., EGP)";
        else
            CurrencyError = null;
    }

    private void ValidateTimeZone()
    {
        if (string.IsNullOrWhiteSpace(DefaultTimeZone))
            TimeZoneError = "Timezone is required";
        else
            TimeZoneError = null;
    }

    private void ValidateSyncConfig()
    {
        SyncBaseUrlError = null;
        SyncApiKeyError = null;
        SyncVaultIdError = null;

        var hasBaseUrl = !string.IsNullOrWhiteSpace(SyncBaseUrl);
        var hasApiKey = !string.IsNullOrWhiteSpace(SyncApiKey);
        var hasVaultId = !string.IsNullOrWhiteSpace(SyncVaultId);

        if (hasBaseUrl || hasApiKey || hasVaultId)
        {
            if (hasBaseUrl)
            {
                if (!Uri.TryCreate(SyncBaseUrl, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != "http" && uri.Scheme != "https"))
                {
                    SyncBaseUrlError = "Must be a valid HTTP(S) URL";
                }
            }
            else if (hasApiKey || hasVaultId)
            {
                SyncBaseUrlError = "Base URL is required when API Key or Vault ID is set";
            }

            if (hasBaseUrl && !hasApiKey)
            {
                SyncApiKeyError = "API Key is required when Base URL is set";
            }

            if (hasVaultId && !Guid.TryParse(SyncVaultId, out _))
            {
                SyncVaultIdError = "Vault ID must be a valid GUID";
            }
        }
    }
}
