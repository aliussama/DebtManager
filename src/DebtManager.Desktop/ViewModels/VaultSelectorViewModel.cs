using DebtManager.Desktop.Services;
using DebtManager.Domain.Vault;
using DebtManager.Infrastructure.Vault;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class VaultSelectorViewModel : ObservableObject
{
    private readonly VaultRegistry? _registry;
    private readonly IToastService? _toastService;
    private readonly Action<Guid>? _onVaultOpened;

    public VaultSelectorViewModel(
        VaultRegistry? registry = null,
        IToastService? toastService = null,
        Action<Guid>? onVaultOpened = null)
    {
        _registry = registry;
        _toastService = toastService;
        _onVaultOpened = onVaultOpened;

        LoadVaultsCommand = new AsyncRelayCommand(LoadVaultsAsync);
        CreateVaultCommand = new AsyncRelayCommand(CreateVaultAsync, () => !string.IsNullOrWhiteSpace(NewVaultName));
        RenameVaultCommand = new AsyncRelayCommand(RenameVaultAsync, () => SelectedVault != null && !string.IsNullOrWhiteSpace(RenameText));
        ArchiveVaultCommand = new AsyncRelayCommand(ArchiveVaultAsync, () => SelectedVault != null);
        OpenVaultCommand = new RelayCommand<VaultDescriptor>(OpenVault);
        ShowCreateFormCommand = new RelayCommand(() => IsCreateFormVisible = true);
        CancelCreateCommand = new RelayCommand(CancelCreate);
    }

    public ICommand LoadVaultsCommand { get; }
    public ICommand CreateVaultCommand { get; }
    public ICommand RenameVaultCommand { get; }
    public ICommand ArchiveVaultCommand { get; }
    public ICommand OpenVaultCommand { get; }
    public ICommand ShowCreateFormCommand { get; }
    public ICommand CancelCreateCommand { get; }

    public ObservableCollection<VaultDescriptor> Vaults { get; } = new();

    private VaultDescriptor? _selectedVault;
    public VaultDescriptor? SelectedVault
    {
        get => _selectedVault;
        set => SetProperty(ref _selectedVault, value);
    }

    private string _newVaultName = string.Empty;
    public string NewVaultName
    {
        get => _newVaultName;
        set => SetProperty(ref _newVaultName, value);
    }

    private string _newVaultCurrency = "EGP";
    public string NewVaultCurrency
    {
        get => _newVaultCurrency;
        set => SetProperty(ref _newVaultCurrency, value);
    }

    private string _renameText = string.Empty;
    public string RenameText
    {
        get => _renameText;
        set => SetProperty(ref _renameText, value);
    }

    private bool _isCreateFormVisible;
    public bool IsCreateFormVisible
    {
        get => _isCreateFormVisible;
        set => SetProperty(ref _isCreateFormVisible, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public ObservableCollection<string> CurrencyOptions { get; } = new() { "EGP", "USD", "EUR", "GBP", "SAR", "AED" };

    public async Task LoadVaultsAsync()
    {
        if (_registry == null) return;
        IsLoading = true;
        try
        {
            var vaults = await _registry.ListVaultsAsync();
            Vaults.Clear();
            foreach (var v in vaults.Where(v => !v.IsArchived))
                Vaults.Add(v);
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to load vaults", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task CreateVaultAsync()
    {
        if (_registry == null || string.IsNullOrWhiteSpace(NewVaultName)) return;
        try
        {
            await _registry.CreateVaultAsync(NewVaultName.Trim(), NewVaultCurrency);
            _toastService?.Success($"Vault '{NewVaultName.Trim()}' created");
            CancelCreate();
            await LoadVaultsAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to create vault", ex);
        }
    }

    private async Task RenameVaultAsync()
    {
        if (_registry == null || SelectedVault == null || string.IsNullOrWhiteSpace(RenameText)) return;
        try
        {
            await _registry.RenameVaultAsync(SelectedVault.VaultId, RenameText.Trim());
            _toastService?.Success($"Vault renamed to '{RenameText.Trim()}'");
            RenameText = string.Empty;
            await LoadVaultsAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to rename vault", ex);
        }
    }

    private async Task ArchiveVaultAsync()
    {
        if (_registry == null || SelectedVault == null) return;
        try
        {
            await _registry.ArchiveVaultAsync(SelectedVault.VaultId, "Archived by user");
            _toastService?.Success($"Vault '{SelectedVault.Name}' archived");
            SelectedVault = null;
            await LoadVaultsAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to archive vault", ex);
        }
    }

    private void OpenVault(VaultDescriptor? vault)
    {
        if (vault == null) return;
        _onVaultOpened?.Invoke(vault.VaultId);
    }

    private void CancelCreate()
    {
        IsCreateFormVisible = false;
        NewVaultName = string.Empty;
        NewVaultCurrency = "EGP";
    }
}
