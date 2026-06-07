using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class ImportViewModel : ObservableObject
{
    private readonly CreateBankImportProfileHandler? _createProfileHandler;
    private readonly ModifyBankImportProfileHandler? _modifyProfileHandler;
    private readonly ArchiveBankImportProfileHandler? _archiveProfileHandler;
    private readonly GetBankImportProfilesListHandler? _getProfilesHandler;
    private readonly PreviewBankImportHandler? _previewHandler;
    private readonly StartBankImportBatchHandler? _importHandler;
    private readonly GetReconciliationCandidatesHandler? _reconcileHandler;
    private readonly ApplyImportedTransactionHandler? _applyHandler;
    private readonly ConfirmMatchImportedTransactionHandler? _matchHandler;
    private readonly IgnoreImportedTransactionHandler? _ignoreHandler;
    private readonly RevertImportedDecisionHandler? _revertHandler;
    private readonly CorrectImportedDecisionHandler? _correctHandler;
    private readonly UndoImportBatchHandler? _undoBatchHandler;
    private readonly BulkApplyUnmatchedHandler? _bulkApplyHandler;
    private readonly GetAccountsListHandler? _accountsHandler;
    private readonly GetImportSuggestionsHandler? _suggestionsHandler;
    private readonly ApplySuggestionHandler? _applySuggestionHandler;
    private readonly RunAutoApplyForBatchHandler? _autoApplyHandler;
    private readonly RecordSplitExpenseHandler? _splitExpenseHandler;
    private readonly GetCategoriesListHandler? _categoriesHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;
    private readonly IFileDialogService? _fileDialogService;

    public ImportViewModel(
        CreateBankImportProfileHandler? createProfileHandler = null,
        ModifyBankImportProfileHandler? modifyProfileHandler = null,
        ArchiveBankImportProfileHandler? archiveProfileHandler = null,
        GetBankImportProfilesListHandler? getProfilesHandler = null,
        PreviewBankImportHandler? previewHandler = null,
        StartBankImportBatchHandler? importHandler = null,
        GetReconciliationCandidatesHandler? reconcileHandler = null,
        ApplyImportedTransactionHandler? applyHandler = null,
        ConfirmMatchImportedTransactionHandler? matchHandler = null,
        IgnoreImportedTransactionHandler? ignoreHandler = null,
        RevertImportedDecisionHandler? revertHandler = null,
        CorrectImportedDecisionHandler? correctHandler = null,
        UndoImportBatchHandler? undoBatchHandler = null,
        BulkApplyUnmatchedHandler? bulkApplyHandler = null,
        GetAccountsListHandler? accountsHandler = null,
        GetImportSuggestionsHandler? suggestionsHandler = null,
        ApplySuggestionHandler? applySuggestionHandler = null,
        RunAutoApplyForBatchHandler? autoApplyHandler = null,
        RecordSplitExpenseHandler? splitExpenseHandler = null,
        GetCategoriesListHandler? categoriesHandler = null,
        Guid actorUserId = default,
        Guid deviceId = default,
        IToastService? toastService = null,
        IExportService? exportService = null,
        IFileDialogService? fileDialogService = null)
    {
        _createProfileHandler = createProfileHandler;
        _modifyProfileHandler = modifyProfileHandler;
        _archiveProfileHandler = archiveProfileHandler;
        _getProfilesHandler = getProfilesHandler;
        _previewHandler = previewHandler;
        _importHandler = importHandler;
        _reconcileHandler = reconcileHandler;
        _applyHandler = applyHandler;
        _matchHandler = matchHandler;
        _ignoreHandler = ignoreHandler;
        _revertHandler = revertHandler;
        _correctHandler = correctHandler;
        _undoBatchHandler = undoBatchHandler;
        _bulkApplyHandler = bulkApplyHandler;
        _accountsHandler = accountsHandler;
        _suggestionsHandler = suggestionsHandler;
        _applySuggestionHandler = applySuggestionHandler;
        _autoApplyHandler = autoApplyHandler;
        _splitExpenseHandler = splitExpenseHandler;
        _categoriesHandler = categoriesHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;
        _fileDialogService = fileDialogService;

        // Tab commands
        SwitchToProfilesCommand = new RelayCommand(() => SelectedTab = "Profiles");
        SwitchToImportCommand = new RelayCommand(() => SelectedTab = "Import");

        // Profile commands
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ShowCreateProfileCommand = new RelayCommand(() => IsCreateProfileVisible = true);
        CancelCreateProfileCommand = new RelayCommand(CancelCreateProfile);
        ConfirmCreateProfileCommand = new AsyncRelayCommand(ConfirmCreateProfileAsync);
        ArchiveProfileCommand = new RelayCommand<ImportProfileDto>(p => _ = ArchiveProfileAsync(p));
        SaveProfileCommand = new RelayCommand<ImportProfileDto>(p => _ = SaveProfileAsync(p));

        // Import commands
        PickCsvFileCommand = new RelayCommand(PickCsvFile);
        PreviewImportCommand = new AsyncRelayCommand(PreviewImportAsync);
        RunImportCommand = new AsyncRelayCommand(RunImportAsync);
        LoadReconciliationCommand = new AsyncRelayCommand(LoadReconciliationAsync);
        ExportReconciliationCommand = new AsyncRelayCommand(ExportReconciliationAsync);

        // Reconciliation row actions
        ApplyAsIncomeCommand = new RelayCommand<ReconciliationRowDto>(r => _ = ApplyAsync(r, "Income"));
        ApplyAsExpenseCommand = new RelayCommand<ReconciliationRowDto>(r => _ = ApplyAsync(r, "Expense"));
        MatchCommand = new RelayCommand<ReconciliationRowDto>(r => _ = MatchAsync(r));
        IgnoreCommand = new RelayCommand<ReconciliationRowDto>(r => _ = IgnoreAsync(r));

        // Undo / Correction commands
        UndoDecisionCommand = new RelayCommand<ReconciliationRowDto>(r => _ = UndoDecisionAsync(r));
        CorrectToIncomeCommand = new RelayCommand<ReconciliationRowDto>(r => _ = CorrectAsync(r, "apply", "Income"));
        CorrectToExpenseCommand = new RelayCommand<ReconciliationRowDto>(r => _ = CorrectAsync(r, "apply", "Expense"));
        CorrectToIgnoreCommand = new RelayCommand<ReconciliationRowDto>(r => _ = CorrectAsync(r, "ignore", null));
        UndoBatchCommand = new AsyncRelayCommand(UndoBatchAsync);
        BulkApplyCommand = new AsyncRelayCommand(BulkApplyAsync);
        GenerateSuggestionsCommand = new AsyncRelayCommand(GenerateSuggestionsAsync);
        ApplySuggestionCommand = new RelayCommand<ImportSuggestionDto>(s => _ = ApplySuggestionAsync(s));
        RunAutoApplyCommand = new AsyncRelayCommand(RunAutoApplyAsync);

        // Split-before-apply commands
        StartSplitForSelectedRowCommand = new RelayCommand<ReconciliationRowDto>(StartSplitForRow);
        CancelSplitForSelectedRowCommand = new RelayCommand(CancelSplitForRow);
        AddImportSplitLineCommand = new RelayCommand(AddImportSplitLine);
        RemoveImportSplitLineCommand = new RelayCommand<SplitExpenseLineVm>(RemoveImportSplitLine);
        ApplySplitCommand = new AsyncRelayCommand(ApplySplitAsync);

        SelectedTab = "Import";
    }

    // Commands
    public ICommand SwitchToProfilesCommand { get; }
    public ICommand SwitchToImportCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ShowCreateProfileCommand { get; }
    public ICommand CancelCreateProfileCommand { get; }
    public ICommand ConfirmCreateProfileCommand { get; }
    public ICommand ArchiveProfileCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand PickCsvFileCommand { get; }
    public ICommand PreviewImportCommand { get; }
    public ICommand RunImportCommand { get; }
    public ICommand LoadReconciliationCommand { get; }
    public ICommand ExportReconciliationCommand { get; }
    public ICommand ApplyAsIncomeCommand { get; }
    public ICommand ApplyAsExpenseCommand { get; }
    public ICommand MatchCommand { get; }
    public ICommand IgnoreCommand { get; }
    public ICommand UndoDecisionCommand { get; }
    public ICommand CorrectToIncomeCommand { get; }
    public ICommand CorrectToExpenseCommand { get; }
    public ICommand CorrectToIgnoreCommand { get; }
    public ICommand UndoBatchCommand { get; }
    public ICommand BulkApplyCommand { get; }
    public ICommand GenerateSuggestionsCommand { get; }
    public ICommand ApplySuggestionCommand { get; }
    public ICommand RunAutoApplyCommand { get; }
    public ICommand StartSplitForSelectedRowCommand { get; }
    public ICommand CancelSplitForSelectedRowCommand { get; }
    public ICommand AddImportSplitLineCommand { get; }
    public ICommand RemoveImportSplitLineCommand { get; }
    public ICommand ApplySplitCommand { get; }

    // Collections
    public ObservableCollection<ImportProfileDto> Profiles { get; } = new();
    public ObservableCollection<AccountListItemDto> Accounts { get; } = new();
    public ObservableCollection<ImportPreviewRowDto> PreviewRows { get; } = new();
    public ObservableCollection<ReconciliationRowDto> ReconciliationRows { get; } = new();

    // Tab state
    private string _selectedTab = "Import";
    public string SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    // Loading
    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    // Profile create form
    private bool _isCreateProfileVisible;
    public bool IsCreateProfileVisible { get => _isCreateProfileVisible; set => SetProperty(ref _isCreateProfileVisible, value); }

    private string _newProfileName = string.Empty;
    public string NewProfileName { get => _newProfileName; set => SetProperty(ref _newProfileName, value); }

    private string _newProfileMappingJson = "{\n  \"Delimiter\": \",\",\n  \"HasHeaderRow\": true,\n  \"DateColumn\": 0,\n  \"AmountColumn\": 1,\n  \"DescriptionColumn\": 2,\n  \"DateFormat\": \"yyyy-MM-dd\",\n  \"DecimalSeparator\": \".\",\n  \"CurrencyCode\": \"EGP\",\n  \"SignConvention\": \"negative_is_debit\"\n}";
    public string NewProfileMappingJson { get => _newProfileMappingJson; set => SetProperty(ref _newProfileMappingJson, value); }

    // Import form
    private AccountListItemDto? _selectedAccount;
    public AccountListItemDto? SelectedAccount { get => _selectedAccount; set => SetProperty(ref _selectedAccount, value); }

    private ImportProfileDto? _selectedProfile;
    public ImportProfileDto? SelectedProfile { get => _selectedProfile; set => SetProperty(ref _selectedProfile, value); }

    private string _csvFilePath = string.Empty;
    public string CsvFilePath { get => _csvFilePath; set => SetProperty(ref _csvFilePath, value); }

    private string? _csvContent;

    // Preview state
    private bool _hasPreview;
    public bool HasPreview { get => _hasPreview; set => SetProperty(ref _hasPreview, value); }

    private int _previewTotalRows;
    public int PreviewTotalRows { get => _previewTotalRows; set => SetProperty(ref _previewTotalRows, value); }

    private int _previewDuplicateRows;
    public int PreviewDuplicateRows { get => _previewDuplicateRows; set => SetProperty(ref _previewDuplicateRows, value); }

    private bool _previewIsDuplicateBatch;
    public bool PreviewIsDuplicateBatch { get => _previewIsDuplicateBatch; set => SetProperty(ref _previewIsDuplicateBatch, value); }

    // Import completed state
    private bool _importCompleted;
    public bool ImportCompleted { get => _importCompleted; set => SetProperty(ref _importCompleted, value); }

    private Guid _lastImportedBatchId;

    // Empty states
    private bool _isProfilesEmpty;
    public bool IsProfilesEmpty { get => _isProfilesEmpty; set => SetProperty(ref _isProfilesEmpty, value); }

    private bool _isReconciliationEmpty;
    public bool IsReconciliationEmpty { get => _isReconciliationEmpty; set => SetProperty(ref _isReconciliationEmpty, value); }

    // Suggestions
    public ObservableCollection<ImportSuggestionDto> Suggestions { get; } = new();

    private bool _isSuggestionsEmpty;
    public bool IsSuggestionsEmpty { get => _isSuggestionsEmpty; set => SetProperty(ref _isSuggestionsEmpty, value); }

    private bool _autoApplyEnabled;
    public bool AutoApplyEnabled { get => _autoApplyEnabled; set => SetProperty(ref _autoApplyEnabled, value); }

    private int _autoApplyThreshold = 80;
    public int AutoApplyThreshold { get => _autoApplyThreshold; set => SetProperty(ref _autoApplyThreshold, value); }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            await LoadProfilesAsync();
            await LoadAccountsAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to load import data", ex); }
        finally { IsLoading = false; }
    }

    private async Task LoadProfilesAsync()
    {
        if (_getProfilesHandler == null) return;
        var profiles = await _getProfilesHandler.HandleAsync(CancellationToken.None);
        Profiles.Clear();
        foreach (var p in profiles) Profiles.Add(p);
        IsProfilesEmpty = Profiles.Count == 0;
    }

    private async Task LoadAccountsAsync()
    {
        if (_accountsHandler == null) return;
        var accounts = await _accountsHandler.HandleAsync(CancellationToken.None);
        Accounts.Clear();
        foreach (var a in accounts.Where(a => !a.IsArchived)) Accounts.Add(a);
    }

    private void CancelCreateProfile()
    {
        IsCreateProfileVisible = false;
        NewProfileName = string.Empty;
        NewProfileMappingJson = "{\n  \"Delimiter\": \",\",\n  \"HasHeaderRow\": true,\n  \"DateColumn\": 0,\n  \"AmountColumn\": 1,\n  \"DescriptionColumn\": 2,\n  \"DateFormat\": \"yyyy-MM-dd\",\n  \"DecimalSeparator\": \".\",\n  \"CurrencyCode\": \"EGP\",\n  \"SignConvention\": \"negative_is_debit\"\n}";
    }

    private async Task ConfirmCreateProfileAsync()
    {
        if (_createProfileHandler == null || string.IsNullOrWhiteSpace(NewProfileName)) return;

        try
        {
            // Validate JSON
            System.Text.Json.JsonDocument.Parse(NewProfileMappingJson);
        }
        catch
        {
            _toastService?.Error("Invalid JSON mapping");
            return;
        }

        try
        {
            await _createProfileHandler.HandleAsync(
                new CreateBankImportProfileCommand(NewProfileName, NewProfileMappingJson,
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Import profile created");
            CancelCreateProfile();
            await LoadProfilesAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to create profile", ex); }
    }

    private async Task ArchiveProfileAsync(ImportProfileDto? profile)
    {
        if (_archiveProfileHandler == null || profile == null) return;
        try
        {
            await _archiveProfileHandler.HandleAsync(
                new ArchiveBankImportProfileCommand(profile.ProfileId,
                    DateOnly.FromDateTime(DateTime.Today), "Archived by user"),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Profile archived");
            await LoadProfilesAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to archive profile", ex); }
    }

    private async Task SaveProfileAsync(ImportProfileDto? profile)
    {
        if (_modifyProfileHandler == null || profile == null) return;
        try
        {
            await _modifyProfileHandler.HandleAsync(
                new ModifyBankImportProfileCommand(profile.ProfileId, profile.MappingJson,
                    DateOnly.FromDateTime(DateTime.Today), "Updated via UI"),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Profile updated");
            await LoadProfilesAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to update profile", ex); }
    }

    private void PickCsvFile()
    {
        var path = _fileDialogService?.ShowOpenFileDialog("Select CSV File", "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*");
        if (string.IsNullOrEmpty(path)) return;
        CsvFilePath = path;
        _csvContent = File.ReadAllText(path);
        HasPreview = false;
        ImportCompleted = false;
        PreviewRows.Clear();
        ReconciliationRows.Clear();
    }

    private async Task PreviewImportAsync()
    {
        if (_previewHandler == null || SelectedProfile == null || SelectedAccount == null ||
            string.IsNullOrEmpty(_csvContent))
        {
            _toastService?.Error("Select account, profile, and CSV file first");
            return;
        }

        IsLoading = true;
        try
        {
            var result = await _previewHandler.HandleAsync(
                new PreviewBankImportCommand(SelectedProfile.ProfileId, SelectedAccount.AccountId, _csvContent),
                CancellationToken.None);

            PreviewRows.Clear();
            foreach (var r in result.Rows) PreviewRows.Add(r);
            PreviewTotalRows = result.TotalRows;
            PreviewDuplicateRows = result.DuplicateRows;
            PreviewIsDuplicateBatch = result.IsDuplicateBatch;
            HasPreview = true;
        }
        catch (Exception ex) { _toastService?.Error("Preview failed", ex); }
        finally { IsLoading = false; }
    }

    private async Task RunImportAsync()
    {
        if (_importHandler == null || SelectedProfile == null || SelectedAccount == null ||
            string.IsNullOrEmpty(_csvContent))
            return;

        if (PreviewIsDuplicateBatch)
        {
            _toastService?.Error("This file has already been imported for this account/profile");
            return;
        }

        IsLoading = true;
        try
        {
            var fileName = Path.GetFileName(CsvFilePath);
            _lastImportedBatchId = await _importHandler.HandleAsync(
                new StartBankImportBatchCommand(SelectedProfile.ProfileId, SelectedAccount.AccountId,
                    fileName, _csvContent!, DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);

            _toastService?.Success("CSV imported successfully");
            ImportCompleted = true;

            // Auto-load reconciliation
            await LoadReconciliationAsync();
        }
        catch (Exception ex) { _toastService?.Error("Import failed", ex); }
        finally { IsLoading = false; }
    }

    private async Task LoadReconciliationAsync()
    {
        if (_reconcileHandler == null || SelectedAccount == null) return;

        IsLoading = true;
        try
        {
            var rows = await _reconcileHandler.HandleAsync(SelectedAccount.AccountId, CancellationToken.None);
            ReconciliationRows.Clear();
            foreach (var r in rows) ReconciliationRows.Add(r);
            IsReconciliationEmpty = ReconciliationRows.Count == 0;
        }
        catch (Exception ex) { _toastService?.Error("Failed to load reconciliation", ex); }
        finally { IsLoading = false; }
    }

    private async Task ApplyAsync(ReconciliationRowDto? row, string mode)
    {
        if (_applyHandler == null || row == null) return;
        try
        {
            await _applyHandler.HandleAsync(
                new ApplyImportedTransactionCommand(row.ImportedId, mode, null, null, null),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success($"Applied as {mode}");
            await LoadReconciliationAsync();
        }
        catch (Exception ex) { _toastService?.Error(ex.Message); }
    }

    private async Task MatchAsync(ReconciliationRowDto? row)
    {
        if (_matchHandler == null || row == null || !row.MatchedEventId.HasValue) return;
        try
        {
            await _matchHandler.HandleAsync(
                new ConfirmMatchImportedTransactionCommand(row.ImportedId, row.MatchedEventId.Value, null),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Matched");
            await LoadReconciliationAsync();
        }
        catch (Exception ex) { _toastService?.Error(ex.Message); }
    }

    private async Task IgnoreAsync(ReconciliationRowDto? row)
    {
        if (_ignoreHandler == null || row == null) return;
        try
        {
            await _ignoreHandler.HandleAsync(
                new IgnoreImportedTransactionCommand(row.ImportedId, "Ignored by user"),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Ignored");
            await LoadReconciliationAsync();
        }
        catch (Exception ex) { _toastService?.Error(ex.Message); }
    }

    private async Task ExportReconciliationAsync()
    {
        if (_exportService == null || ReconciliationRows.Count == 0) return;
        try
        {
            var headers = new[] { "Date", "Amount", "Currency", "Description", "Direction", "Status", "MatchType", "Confidence" };
            var rows = ReconciliationRows.Select(r => (IReadOnlyList<string?>)new[]
            {
                r.TxnDate.ToString("yyyy-MM-dd"), r.Amount.ToString("F2"), r.CurrencyCode,
                r.Description, r.Direction, r.Status, r.MatchType ?? "", r.Confidence.ToString("F2")
            }).ToList();
            await _exportService.ExportCsvAsync("Reconciliation", headers, rows);
            _toastService?.Success("Reconciliation exported");
        }
        catch (Exception ex) { _toastService?.Error("Export failed", ex); }
    }

    private async Task UndoDecisionAsync(ReconciliationRowDto? row)
    {
        if (_revertHandler == null || row == null) return;
        try
        {
            await _revertHandler.HandleAsync(
                new RevertImportedDecisionCommand(row.ImportedId, DateOnly.FromDateTime(DateTime.Today), "Undone by user"),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Decision reverted");
            await LoadReconciliationAsync();
        }
        catch (Exception ex) { _toastService?.Error(ex.Message); }
    }

    private async Task CorrectAsync(ReconciliationRowDto? row, string newDecisionType, string? applyMode)
    {
        if (_correctHandler == null || row == null) return;
        try
        {
            await _correctHandler.HandleAsync(
                new CorrectImportedDecisionCommand(row.ImportedId, newDecisionType, applyMode, null,
                    DateOnly.FromDateTime(DateTime.Today), "Corrected by user"),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success($"Corrected to {newDecisionType}");
            await LoadReconciliationAsync();
        }
        catch (Exception ex) { _toastService?.Error(ex.Message); }
    }

    private async Task UndoBatchAsync()
    {
        if (_undoBatchHandler == null || _lastImportedBatchId == Guid.Empty) return;
        IsLoading = true;
        try
        {
            var count = await _undoBatchHandler.HandleAsync(
                new UndoImportBatchCommand(_lastImportedBatchId, DateOnly.FromDateTime(DateTime.Today), "Batch undo by user"),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success($"Batch undo complete: {count} decisions reverted");
            await LoadReconciliationAsync();
        }
        catch (Exception ex) { _toastService?.Error(ex.Message); }
        finally { IsLoading = false; }
    }

    private async Task BulkApplyAsync()
    {
        if (_bulkApplyHandler == null || _lastImportedBatchId == Guid.Empty || SelectedAccount == null) return;
        IsLoading = true;
        try
        {
            var count = await _bulkApplyHandler.HandleAsync(
                new BulkApplyUnmatchedCommand(_lastImportedBatchId, SelectedAccount.AccountId,
                    DateOnly.FromDateTime(DateTime.Today), "Bulk apply by user", true),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success($"Bulk applied {count} transactions");
            await LoadReconciliationAsync();
        }
        catch (Exception ex) { _toastService?.Error(ex.Message); }
        finally { IsLoading = false; }
    }

    private async Task GenerateSuggestionsAsync()
    {
        if (_suggestionsHandler == null || _lastImportedBatchId == Guid.Empty) return;
        IsLoading = true;
        try
        {
            var suggestions = await _suggestionsHandler.HandleAsync(
                _lastImportedBatchId, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
            Suggestions.Clear();
            foreach (var s in suggestions) Suggestions.Add(s);
            IsSuggestionsEmpty = Suggestions.Count == 0;
            if (!IsSuggestionsEmpty)
                _toastService?.Success($"Generated {suggestions.Count} suggestions");
            else
                _toastService?.Info("No suggestions generated (check rules and unresolved transactions)");
        }
        catch (Exception ex) { _toastService?.Error("Failed to generate suggestions", ex); }
        finally { IsLoading = false; }
    }

    private async Task ApplySuggestionAsync(ImportSuggestionDto? suggestion)
    {
        if (_applySuggestionHandler == null || suggestion == null) return;
        try
        {
            await _applySuggestionHandler.HandleAsync(
                new ApplySuggestionCommand(
                    suggestion.ImportedTransactionId,
                    suggestion.SuggestionId,
                    suggestion.Kind,
                    suggestion.ProposedAccountId,
                    suggestion.ProposedCategory,
                    suggestion.ProposedRelatedEntityId,
                    suggestion.Notes,
                    false),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Suggestion applied");
            await LoadReconciliationAsync();
            await GenerateSuggestionsAsync();
        }
        catch (Exception ex) { _toastService?.Error(ex.Message); }
    }

    private async Task RunAutoApplyAsync()
    {
        if (_autoApplyHandler == null || _lastImportedBatchId == Guid.Empty) return;
        if (!AutoApplyEnabled)
        {
            _toastService?.Error("Auto-apply is not enabled");
            return;
        }
        IsLoading = true;
        try
        {
            var count = await _autoApplyHandler.HandleAsync(
                new RunAutoApplyCommand(_lastImportedBatchId, true, AutoApplyThreshold,
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success($"Auto-applied {count} transactions");
            await LoadReconciliationAsync();
            await GenerateSuggestionsAsync();
        }
        catch (Exception ex) { _toastService?.Error(ex.Message); }
        finally { IsLoading = false; }
    }

    // --- Split-before-apply state ---
    private bool _isImportSplitMode;
    public bool IsImportSplitMode { get => _isImportSplitMode; set => SetProperty(ref _isImportSplitMode, value); }

    public ObservableCollection<SplitExpenseLineVm> ImportSplitLines { get; } = new();
    public ObservableCollection<string> ImportSplitCategories { get; } = new();

    private ReconciliationRowDto? _splitTargetRow;
    public ReconciliationRowDto? SplitTargetRow { get => _splitTargetRow; set => SetProperty(ref _splitTargetRow, value); }

    public decimal ImportSplitTotal => ImportSplitLines.Sum(l => l.Amount);

    private string? _importSplitError;
    public string? ImportSplitError { get => _importSplitError; set => SetProperty(ref _importSplitError, value); }

    private void StartSplitForRow(ReconciliationRowDto? row)
    {
        if (row == null) return;
        SplitTargetRow = row;
        IsImportSplitMode = true;
        ImportSplitLines.Clear();
        ImportSplitLines.Add(new SplitExpenseLineVm());
        ImportSplitLines.Add(new SplitExpenseLineVm());
        ImportSplitError = null;
        _ = LoadImportSplitCategoriesAsync();
    }

    private void CancelSplitForRow()
    {
        IsImportSplitMode = false;
        SplitTargetRow = null;
        ImportSplitLines.Clear();
        ImportSplitError = null;
    }

    private void AddImportSplitLine() => ImportSplitLines.Add(new SplitExpenseLineVm());

    private void RemoveImportSplitLine(SplitExpenseLineVm? line)
    {
        if (line != null && ImportSplitLines.Count > 2)
            ImportSplitLines.Remove(line);
    }

    private async Task ApplySplitAsync()
    {
        if (_splitExpenseHandler == null || SplitTargetRow == null || SelectedAccount == null) return;

        try
        {
            var lines = ImportSplitLines.Select(l =>
                new SplitLineDto(l.Category, l.Amount, string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes)).ToList();
            var total = lines.Sum(l => l.Amount);

            if (total != SplitTargetRow.Amount)
            {
                ImportSplitError = $"Sum of lines ({total:N2}) must equal imported amount ({SplitTargetRow.Amount:N2}).";
                return;
            }

            await _splitExpenseHandler.HandleAsync(new RecordSplitExpenseCommand(
                SelectedAccount.AccountId,
                SplitTargetRow.TxnDate,
                total,
                SplitTargetRow.CurrencyCode,
                lines,
                $"Import split: {SplitTargetRow.Description}"),
                _actorUserId, _deviceId, CancellationToken.None);

            // Mark imported row as applied via the normal apply handler (to update import state)
            if (_applyHandler != null)
            {
                try
                {
                    await _applyHandler.HandleAsync(
                        new ApplyImportedTransactionCommand(
                            SplitTargetRow.ImportedId, "Expense", null,
                            $"Split applied: {lines.Count} lines", null),
                        _actorUserId, _deviceId, CancellationToken.None);
                }
                catch
                {
                    // Already applied or state conflict - the split expense event is already written
                }
            }

            _toastService?.Success("Split expense applied from import");
            CancelSplitForRow();
            await LoadReconciliationAsync();
        }
        catch (Exception ex)
        {
            ImportSplitError = ex.Message;
            _toastService?.Error("Split apply failed", ex);
        }
    }

    private async Task LoadImportSplitCategoriesAsync()
    {
        if (_categoriesHandler == null) return;
        try
        {
            var cats = await _categoriesHandler.HandleAsync(CancellationToken.None);
            ImportSplitCategories.Clear();
            foreach (var c in cats.Where(c => !c.IsArchived && c.Kind == "expense"))
                ImportSplitCategories.Add(c.Name);
        }
        catch { /* Non-critical */ }
    }
}
