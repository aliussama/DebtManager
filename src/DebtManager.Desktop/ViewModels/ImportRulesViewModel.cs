using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class ImportRulesViewModel : ObservableObject
{
    private readonly CreateImportRulePackHandler? _createPackHandler;
    private readonly ModifyImportRulePackHandler? _modifyPackHandler;
    private readonly ArchiveImportRulePackHandler? _archivePackHandler;
    private readonly DefineImportRuleHandler? _defineRuleHandler;
    private readonly ArchiveImportRuleHandler? _archiveRuleHandler;
    private readonly GetImportRulePacksListHandler? _getPacksHandler;
    private readonly GetImportRulePackDetailHandler? _getDetailHandler;
    private readonly PreviewRuleAgainstBatchHandler? _previewHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;

    public ImportRulesViewModel(
        CreateImportRulePackHandler? createPackHandler = null,
        ModifyImportRulePackHandler? modifyPackHandler = null,
        ArchiveImportRulePackHandler? archivePackHandler = null,
        DefineImportRuleHandler? defineRuleHandler = null,
        ArchiveImportRuleHandler? archiveRuleHandler = null,
        GetImportRulePacksListHandler? getPacksHandler = null,
        GetImportRulePackDetailHandler? getDetailHandler = null,
        PreviewRuleAgainstBatchHandler? previewHandler = null,
        Guid actorUserId = default,
        Guid deviceId = default,
        IToastService? toastService = null,
        IExportService? exportService = null)
    {
        _createPackHandler = createPackHandler;
        _modifyPackHandler = modifyPackHandler;
        _archivePackHandler = archivePackHandler;
        _defineRuleHandler = defineRuleHandler;
        _archiveRuleHandler = archiveRuleHandler;
        _getPacksHandler = getPacksHandler;
        _getDetailHandler = getDetailHandler;
        _previewHandler = previewHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ShowCreatePackCommand = new RelayCommand(() => IsCreatePackVisible = true);
        CancelCreatePackCommand = new RelayCommand(CancelCreatePack);
        ConfirmCreatePackCommand = new AsyncRelayCommand(ConfirmCreatePackAsync);
        ArchivePackCommand = new RelayCommand<ImportRulePackDto>(p => _ = ArchivePackAsync(p));
        SelectPackCommand = new RelayCommand<ImportRulePackDto>(p => _ = SelectPackAsync(p));
        ShowAddRuleCommand = new RelayCommand(() => IsAddRuleVisible = true);
        CancelAddRuleCommand = new RelayCommand(CancelAddRule);
        ConfirmAddRuleCommand = new AsyncRelayCommand(ConfirmAddRuleAsync);
        ArchiveRuleCommand = new RelayCommand<ImportRuleDto>(r => _ = ArchiveRuleAsync(r));
    }

    // Commands
    public ICommand RefreshCommand { get; }
    public ICommand ShowCreatePackCommand { get; }
    public ICommand CancelCreatePackCommand { get; }
    public ICommand ConfirmCreatePackCommand { get; }
    public ICommand ArchivePackCommand { get; }
    public ICommand SelectPackCommand { get; }
    public ICommand ShowAddRuleCommand { get; }
    public ICommand CancelAddRuleCommand { get; }
    public ICommand ConfirmAddRuleCommand { get; }
    public ICommand ArchiveRuleCommand { get; }

    // Collections
    public ObservableCollection<ImportRulePackDto> Packs { get; } = new();
    public ObservableCollection<ImportRuleDto> SelectedPackRules { get; } = new();

    // State
    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _isPacksEmpty;
    public bool IsPacksEmpty { get => _isPacksEmpty; set => SetProperty(ref _isPacksEmpty, value); }

    private ImportRulePackDto? _selectedPack;
    public ImportRulePackDto? SelectedPack
    {
        get => _selectedPack;
        set
        {
            if (SetProperty(ref _selectedPack, value))
                OnPropertyChanged(nameof(IsPackSelected));
        }
    }
    public bool IsPackSelected => SelectedPack != null;

    // Create Pack form
    private bool _isCreatePackVisible;
    public bool IsCreatePackVisible { get => _isCreatePackVisible; set => SetProperty(ref _isCreatePackVisible, value); }
    private string _newPackName = string.Empty;
    public string NewPackName { get => _newPackName; set => SetProperty(ref _newPackName, value); }
    private string _newPackDescription = string.Empty;
    public string NewPackDescription { get => _newPackDescription; set => SetProperty(ref _newPackDescription, value); }
    private bool _newPackEnabled = true;
    public bool NewPackEnabled { get => _newPackEnabled; set => SetProperty(ref _newPackEnabled, value); }

    // Add Rule form
    private bool _isAddRuleVisible;
    public bool IsAddRuleVisible { get => _isAddRuleVisible; set => SetProperty(ref _isAddRuleVisible, value); }
    private string _newRuleKind = "Categorize";
    public string NewRuleKind { get => _newRuleKind; set => SetProperty(ref _newRuleKind, value); }
    private int _newRulePriority = 10;
    public int NewRulePriority { get => _newRulePriority; set => SetProperty(ref _newRulePriority, value); }
    private string _newRuleMatchSpecJson = "{}";
    public string NewRuleMatchSpecJson { get => _newRuleMatchSpecJson; set => SetProperty(ref _newRuleMatchSpecJson, value); }
    private string _newRuleActionSpecJson = "{}";
    public string NewRuleActionSpecJson { get => _newRuleActionSpecJson; set => SetProperty(ref _newRuleActionSpecJson, value); }
    private bool _newRuleEnabled = true;
    public bool NewRuleEnabled { get => _newRuleEnabled; set => SetProperty(ref _newRuleEnabled, value); }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            if (_getPacksHandler == null) return;
            var packs = await _getPacksHandler.HandleAsync(false, CancellationToken.None);
            Packs.Clear();
            foreach (var p in packs) Packs.Add(p);
            IsPacksEmpty = Packs.Count == 0;
        }
        catch (Exception ex) { _toastService?.Error("Failed to load import rule packs", ex); }
        finally { IsLoading = false; }
    }

    private void CancelCreatePack()
    {
        IsCreatePackVisible = false;
        NewPackName = string.Empty;
        NewPackDescription = string.Empty;
        NewPackEnabled = true;
    }

    private async Task ConfirmCreatePackAsync()
    {
        if (_createPackHandler == null || string.IsNullOrWhiteSpace(NewPackName)) return;
        try
        {
            await _createPackHandler.HandleAsync(
                new CreateImportRulePackCommand(NewPackName, NewPackDescription, NewPackEnabled, DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Import rule pack created");
            CancelCreatePack();
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to create pack", ex); }
    }

    private async Task ArchivePackAsync(ImportRulePackDto? pack)
    {
        if (_archivePackHandler == null || pack == null) return;
        try
        {
            await _archivePackHandler.HandleAsync(
                new ArchiveImportRulePackCommand(pack.PackId, "Archived by user", DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Pack archived");
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to archive pack", ex); }
    }

    private async Task SelectPackAsync(ImportRulePackDto? pack)
    {
        if (_getDetailHandler == null || pack == null) return;
        SelectedPack = pack;
        SelectedPackRules.Clear();
        try
        {
            var detail = await _getDetailHandler.HandleAsync(pack.PackId, CancellationToken.None);
            if (detail == null) return;
            foreach (var r in detail.Rules) SelectedPackRules.Add(r);
        }
        catch (Exception ex) { _toastService?.Error("Failed to load pack detail", ex); }
    }

    private void CancelAddRule()
    {
        IsAddRuleVisible = false;
        NewRuleKind = "Categorize";
        NewRulePriority = 10;
        NewRuleMatchSpecJson = "{}";
        NewRuleActionSpecJson = "{}";
        NewRuleEnabled = true;
    }

    private async Task ConfirmAddRuleAsync()
    {
        if (_defineRuleHandler == null || SelectedPack == null) return;
        try
        {
            System.Text.Json.JsonDocument.Parse(NewRuleMatchSpecJson);
            System.Text.Json.JsonDocument.Parse(NewRuleActionSpecJson);
        }
        catch
        {
            _toastService?.Error("Invalid JSON in match/action spec");
            return;
        }

        try
        {
            await _defineRuleHandler.HandleAsync(
                new DefineImportRuleCommand(SelectedPack.PackId, null, 1, NewRuleKind,
                    NewRuleMatchSpecJson, NewRuleActionSpecJson, NewRulePriority, NewRuleEnabled,
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Rule added");
            CancelAddRule();
            await SelectPackAsync(SelectedPack);
        }
        catch (Exception ex) { _toastService?.Error("Failed to add rule", ex); }
    }

    private async Task ArchiveRuleAsync(ImportRuleDto? rule)
    {
        if (_archiveRuleHandler == null || rule == null) return;
        try
        {
            await _archiveRuleHandler.HandleAsync(
                new ArchiveImportRuleCommand(rule.PackId, rule.RuleId, "Archived by user", DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Rule archived");
            if (SelectedPack != null) await SelectPackAsync(SelectedPack);
        }
        catch (Exception ex) { _toastService?.Error("Failed to archive rule", ex); }
    }
}
