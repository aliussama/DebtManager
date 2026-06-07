using DebtManager.Desktop.Services;
using DebtManager.Infrastructure.Diagnostics;
using DebtManager.Infrastructure.Security;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Settings view.
/// Manages application configuration with validation and persistence.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly SecureConfiguration _config;
    private readonly Action? _onSyncConfigChanged;
    private readonly IToastService? _toastService;
    private readonly Action? _onRunOnboarding;
    private readonly IThemeService? _themeService;
    private readonly IVaultBackupService? _backupService;

    // Default values
    private const string DefaultCurrencyValue = "EGP";
    private const string DefaultTimezoneValue = "Africa/Cairo";

    public SettingsViewModel(SecureConfiguration config, Action? onSyncConfigChanged = null, IToastService? toastService = null, Action? onRunOnboarding = null, IThemeService? themeService = null, IVaultBackupService? backupService = null)
    {
        _config = config;
        _onSyncConfigChanged = onSyncConfigChanged;
        _toastService = toastService;
        _onRunOnboarding = onRunOnboarding;
        _themeService = themeService;
        _backupService = backupService;

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsSaving);
        ResetDefaultsCommand = new RelayCommand(ResetToDefaults);
        RunOnboardingCommand = new RelayCommand(RunOnboarding);
        BackupCommand = new AsyncRelayCommand(BackupAsync, () => !IsBackupBusy);
        RestoreCommand = new RelayCommand(PickRestoreFile, () => !IsRestoreBusy);
        ConfirmRestoreCommand = new AsyncRelayCommand(ConfirmRestoreAsync, () => !IsRestoreBusy);
        CancelRestoreCommand = new RelayCommand(CancelRestore);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
        OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);

        // Initialize available options
        InitializeOptions();

        // Load current settings
        LoadSettings();
    }

    // Commands
    public ICommand SaveCommand { get; }
    public ICommand ResetDefaultsCommand { get; }
    public ICommand RunOnboardingCommand { get; }
    public ICommand BackupCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand ConfirmRestoreCommand { get; }
    public ICommand CancelRestoreCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }

    // Available options
    public ObservableCollection<string> AvailableCurrencies { get; } = new();
    public ObservableCollection<string> AvailableTimezones { get; } = new();
    public ObservableCollection<string> AvailableThemes { get; } = new() { "Dark", "Light" };

    // Theme
    private string _selectedTheme = "Dark";
    public string SelectedTheme
    {
        get => _selectedTheme;
        set => SetProperty(ref _selectedTheme, value);
    }

    // General Settings
    private string _defaultCurrencyCode = DefaultCurrencyValue;
    public string DefaultCurrencyCode
    {
        get => _defaultCurrencyCode;
        set
        {
            if (SetProperty(ref _defaultCurrencyCode, value))
            {
                ValidateCurrency();
                OnPropertyChanged(nameof(HasValidationErrors));
            }
        }
    }

    private string _defaultTimeZone = DefaultTimezoneValue;
    public string DefaultTimeZone
    {
        get => _defaultTimeZone;
        set
        {
            if (SetProperty(ref _defaultTimeZone, value))
            {
                ValidateTimezone();
                OnPropertyChanged(nameof(HasValidationErrors));
            }
        }
    }

    // Sync Settings
    private string _syncBaseUrl = string.Empty;
    public string SyncBaseUrl
    {
        get => _syncBaseUrl;
        set
        {
            if (SetProperty(ref _syncBaseUrl, value))
            {
                ValidateSyncConfig();
                OnPropertyChanged(nameof(HasValidationErrors));
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
                OnPropertyChanged(nameof(HasValidationErrors));
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
                OnPropertyChanged(nameof(HasValidationErrors));
            }
        }
    }

    // Security Settings
    private bool _requireAppUnlock;
    public bool RequireAppUnlock
    {
        get => _requireAppUnlock;
        set => SetProperty(ref _requireAppUnlock, value);
    }

    // Status
    private bool _isSaving;
    public bool IsSaving
    {
        get => _isSaving;
        set => SetProperty(ref _isSaving, value);
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private bool _isStatusSuccess;
    public bool IsStatusSuccess
    {
        get => _isStatusSuccess;
        set => SetProperty(ref _isStatusSuccess, value);
    }

    // Validation errors
    private string? _currencyError;
    public string? CurrencyError
    {
        get => _currencyError;
        set => SetProperty(ref _currencyError, value);
    }

    private string? _timezoneError;
    public string? TimezoneError
    {
        get => _timezoneError;
        set => SetProperty(ref _timezoneError, value);
    }

    private string? _syncBaseUrlError;
    public string? SyncBaseUrlError
    {
        get => _syncBaseUrlError;
        set => SetProperty(ref _syncBaseUrlError, value);
    }

    private string? _syncApiKeyError;
    public string? SyncApiKeyError
    {
        get => _syncApiKeyError;
        set => SetProperty(ref _syncApiKeyError, value);
    }

    private string? _syncVaultIdError;
    public string? SyncVaultIdError
    {
        get => _syncVaultIdError;
        set => SetProperty(ref _syncVaultIdError, value);
    }

    public bool HasValidationErrors =>
        !string.IsNullOrEmpty(CurrencyError) ||
        !string.IsNullOrEmpty(TimezoneError) ||
        !string.IsNullOrEmpty(SyncBaseUrlError) ||
        !string.IsNullOrEmpty(SyncApiKeyError) ||
        !string.IsNullOrEmpty(SyncVaultIdError);

    // Backup & Restore state
    private bool _isBackupBusy;
    public bool IsBackupBusy
    {
        get => _isBackupBusy;
        set => SetProperty(ref _isBackupBusy, value);
    }

    private bool _isRestoreBusy;
    public bool IsRestoreBusy
    {
        get => _isRestoreBusy;
        set => SetProperty(ref _isRestoreBusy, value);
    }

    private bool _isRestoreConfirmVisible;
    public bool IsRestoreConfirmVisible
    {
        get => _isRestoreConfirmVisible;
        set => SetProperty(ref _isRestoreConfirmVisible, value);
    }

    private string _selectedRestoreFilePath = string.Empty;
    public string SelectedRestoreFilePath
    {
        get => _selectedRestoreFilePath;
        set => SetProperty(ref _selectedRestoreFilePath, value);
    }

    private void InitializeOptions()
    {
        // Common currencies
        AvailableCurrencies.Add("EGP");
        AvailableCurrencies.Add("USD");
        AvailableCurrencies.Add("EUR");
        AvailableCurrencies.Add("GBP");
        AvailableCurrencies.Add("SAR");
        AvailableCurrencies.Add("AED");
        AvailableCurrencies.Add("KWD");
        AvailableCurrencies.Add("QAR");

        // Common timezones
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

    public void LoadSettings()
    {
        DefaultCurrencyCode = _config.Get(ConfigKeys.DefaultCurrency) ?? DefaultCurrencyValue;
        DefaultTimeZone = _config.Get(ConfigKeys.DefaultTimeZone) ?? DefaultTimezoneValue;
        SyncBaseUrl = _config.Get(ConfigKeys.SyncBaseUrl) ?? string.Empty;
        SyncApiKey = _config.Get(ConfigKeys.SyncApiKey) ?? string.Empty;
        SyncVaultId = _config.Get(ConfigKeys.SyncVaultId) ?? string.Empty;
        
        var requireUnlockStr = _config.Get(ConfigKeys.RequireAppUnlock);
        RequireAppUnlock = !string.IsNullOrEmpty(requireUnlockStr) && 
                          bool.TryParse(requireUnlockStr, out var val) && val;

        SelectedTheme = _themeService?.CurrentTheme ?? "Dark";

        // Clear any previous status
        StatusMessage = string.Empty;
        ClearValidationErrors();
    }

    private void ClearValidationErrors()
    {
        CurrencyError = null;
        TimezoneError = null;
        SyncBaseUrlError = null;
        SyncApiKeyError = null;
        SyncVaultIdError = null;
    }

    private void ValidateCurrency()
    {
        if (string.IsNullOrWhiteSpace(DefaultCurrencyCode))
        {
            CurrencyError = "Currency code is required";
        }
        else if (!Regex.IsMatch(DefaultCurrencyCode, @"^[A-Z]{3}$"))
        {
            CurrencyError = "Currency must be 3 uppercase letters (e.g., EGP)";
        }
        else
        {
            CurrencyError = null;
        }
    }

    private void ValidateTimezone()
    {
        if (string.IsNullOrWhiteSpace(DefaultTimeZone))
        {
            TimezoneError = "Timezone is required";
        }
        else
        {
            TimezoneError = null;
        }
    }

    private void ValidateSyncConfig()
    {
        // Clear previous errors
        SyncBaseUrlError = null;
        SyncApiKeyError = null;
        SyncVaultIdError = null;

        var hasBaseUrl = !string.IsNullOrWhiteSpace(SyncBaseUrl);
        var hasApiKey = !string.IsNullOrWhiteSpace(SyncApiKey);
        var hasVaultId = !string.IsNullOrWhiteSpace(SyncVaultId);

        // If any sync field is set, validate them
        if (hasBaseUrl || hasApiKey || hasVaultId)
        {
            // Validate Base URL
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

            // Validate API Key
            if (hasBaseUrl && !hasApiKey)
            {
                SyncApiKeyError = "API Key is required when Base URL is set";
            }

            // Validate Vault ID (optional, but must be GUID if provided)
            if (hasVaultId && !Guid.TryParse(SyncVaultId, out _))
            {
                SyncVaultIdError = "Vault ID must be a valid GUID";
            }
        }
    }

    private bool ValidateAll()
    {
        ValidateCurrency();
        ValidateTimezone();
        ValidateSyncConfig();
        OnPropertyChanged(nameof(HasValidationErrors));
        return !HasValidationErrors;
    }

    private async Task SaveAsync()
    {
        if (!ValidateAll())
        {
            StatusMessage = "Please fix validation errors before saving.";
            IsStatusSuccess = false;
            return;
        }

        IsSaving = true;
        StatusMessage = "Saving...";

        try
        {
            await Task.Run(() =>
            {
                // Save general settings
                _config.Set(ConfigKeys.DefaultCurrency, DefaultCurrencyCode.ToUpperInvariant());
                _config.Set(ConfigKeys.DefaultTimeZone, DefaultTimeZone);

                // Save sync settings
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
                    // Clear sync config if base URL is empty
                    _config.Remove(ConfigKeys.SyncBaseUrl);
                    _config.Remove(ConfigKeys.SyncApiKey);
                    _config.Remove(ConfigKeys.SyncVaultId);
                }

                // Save security settings
                _config.Set(ConfigKeys.RequireAppUnlock, RequireAppUnlock.ToString());
            });

            // Apply theme (must be on UI thread)
            try
            {
                _themeService?.ApplyTheme(SelectedTheme);
            }
            catch (Exception ex)
            {
                _toastService?.Error($"Failed to apply theme: {ex.Message}");
            }

            StatusMessage = "Settings saved successfully!";
            IsStatusSuccess = true;
            _toastService?.Success("Settings saved");

            // Notify that sync config may have changed
            _onSyncConfigChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save: {ex.Message}";
            IsStatusSuccess = false;
            _toastService?.Error("Failed to save settings", ex);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void ResetToDefaults()
    {
        DefaultCurrencyCode = DefaultCurrencyValue;
        DefaultTimeZone = DefaultTimezoneValue;
        SyncBaseUrl = string.Empty;
        SyncApiKey = string.Empty;
        SyncVaultId = string.Empty;
        RequireAppUnlock = false;
        SelectedTheme = "Dark";

        ClearValidationErrors();
        StatusMessage = "Reset to defaults. Click Save to persist.";
        IsStatusSuccess = true;
    }

    private async Task BackupAsync()
    {
        if (_backupService == null) return;
        IsBackupBusy = true;
        try
        {
            await _backupService.BackupAsync(CancellationToken.None);
        }
        catch
        {
            // Toast already shown by service
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

    private void PickRestoreFile()
    {
        if (_backupService == null) return;
        var path = _backupService.PickRestoreFile();
        if (string.IsNullOrEmpty(path)) return;
        SelectedRestoreFilePath = path;
        IsRestoreConfirmVisible = true;
    }

    private async Task ConfirmRestoreAsync()
    {
        if (_backupService == null || string.IsNullOrEmpty(SelectedRestoreFilePath)) return;
        IsRestoreConfirmVisible = false;
        IsRestoreBusy = true;
        try
        {
            await _backupService.RestoreAsync(SelectedRestoreFilePath, CancellationToken.None);
        }
        catch
        {
            // Toast already shown by service
        }
        finally
        {
            IsRestoreBusy = false;
            SelectedRestoreFilePath = string.Empty;
        }
    }

    private void CancelRestore()
    {
        IsRestoreConfirmVisible = false;
        SelectedRestoreFilePath = string.Empty;
    }

    private void RunOnboarding()
    {
        _onRunOnboarding?.Invoke();
    }

    private static void OpenDataFolder()
    {
        try
        {
            var dataDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DebtManager");
            System.IO.Directory.CreateDirectory(dataDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dataDir,
                UseShellExecute = true
            });
        }
        catch { /* best-effort */ }
    }

    private static void OpenLogsFolder()
    {
        try
        {
            var logsDir = AppDiagnostics.LogsDirectory;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = logsDir,
                UseShellExecute = true
            });
        }
        catch { /* best-effort */ }
    }
}
