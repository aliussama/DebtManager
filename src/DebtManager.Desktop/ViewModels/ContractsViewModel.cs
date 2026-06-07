using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class ContractsViewModel : ObservableObject
{
    private readonly CreateContractHandler _createHandler;
    private readonly ModifyContractHandler _modifyHandler;
    private readonly ArchiveContractHandler _archiveHandler;
    private readonly GetContractsListHandler _listHandler;
    private readonly GetContractDetailHandler _detailHandler;
    private readonly PreviewContractBillingGenerationHandler _previewGenHandler;
    private readonly GenerateContractBillsHandler _genBillsHandler;
    private readonly GenerateContractInvoicesHandler _genInvoicesHandler;
    private readonly GetPartiesListHandler _partiesHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;
    private readonly TaggingMixin? _tagging;

    public ContractsViewModel(
        CreateContractHandler createHandler,
        ModifyContractHandler modifyHandler,
        ArchiveContractHandler archiveHandler,
        GetContractsListHandler listHandler,
        GetContractDetailHandler detailHandler,
        PreviewContractBillingGenerationHandler previewGenHandler,
        GenerateContractBillsHandler genBillsHandler,
        GenerateContractInvoicesHandler genInvoicesHandler,
        GetPartiesListHandler partiesHandler,
        Guid actorUserId, Guid deviceId,
        IToastService? toastService = null,
        IExportService? exportService = null,
        TaggingMixin? tagging = null)
    {
        _createHandler = createHandler;
        _modifyHandler = modifyHandler;
        _archiveHandler = archiveHandler;
        _listHandler = listHandler;
        _detailHandler = detailHandler;
        _previewGenHandler = previewGenHandler;
        _genBillsHandler = genBillsHandler;
        _genInvoicesHandler = genInvoicesHandler;
        _partiesHandler = partiesHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;
        _tagging = tagging;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        CreateContractCommand = new AsyncRelayCommand(CreateAsync);
        ArchiveContractCommand = new AsyncRelayCommand(ArchiveAsync);
        GenerateBillsCommand = new AsyncRelayCommand(GenerateBillsAsync);
        ExportCsvCommand = new AsyncRelayCommand(ExportAsync);
    }

    public ICommand RefreshCommand { get; }
    public ICommand CreateContractCommand { get; }
    public ICommand ArchiveContractCommand { get; }
    public ICommand GenerateBillsCommand { get; }
    public ICommand ExportCsvCommand { get; }

    public ObservableCollection<ContractListItemDto> Contracts { get; } = new();
    public ObservableCollection<PartyListItemDto> Parties { get; } = new();

    // Tag filter
    public ObservableCollection<string> TagSuggestions { get; } = new();
    private string _selectedTagFilter = string.Empty;
    public string SelectedTagFilter
    {
        get => _selectedTagFilter;
        set { if (SetProperty(ref _selectedTagFilter, value)) _ = ApplyTagFilterAsync(); }
    }
    private HashSet<Guid>? _tagFilteredIds;

    // Selected entity tags
    public ObservableCollection<string> SelectedEntityTags { get; } = new();
    private string _newTagText = string.Empty;
    public string NewTagText { get => _newTagText; set => SetProperty(ref _newTagText, value); }
    public ICommand AddTagCommand => new RelayCommand(() => { _tagging?.AddTag(NewTagText, SelectedEntityTags); NewTagText = string.Empty; });
    public ICommand RemoveTagCommand => new RelayCommand<string>(tag => _tagging?.RemoveTag(tag, SelectedEntityTags));
    public ICommand SaveTagsCommand => new AsyncRelayCommand(async () => { if (_tagging != null && SelectedContract != null) await _tagging.SaveTagsAsync(SelectedContract.ContractId, "Contract", SelectedEntityTags, TagSuggestions); });

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

    private bool _includeArchived;
    public bool IncludeArchived
    {
        get => _includeArchived;
        set { if (SetProperty(ref _includeArchived, value)) _ = LoadAsync(); }
    }

    private ContractListItemDto? _selectedContract;
    public ContractListItemDto? SelectedContract { get => _selectedContract; set => SetProperty(ref _selectedContract, value); }

    // Create form
    private string _newTitle = string.Empty;
    public string NewTitle { get => _newTitle; set => SetProperty(ref _newTitle, value); }

    private string _newType = "Subscription";
    public string NewType { get => _newType; set => SetProperty(ref _newType, value); }

    private string _newCurrency = "EGP";
    public string NewCurrency { get => _newCurrency; set => SetProperty(ref _newCurrency, value); }

    private decimal _newBaseAmount;
    public decimal NewBaseAmount { get => _newBaseAmount; set => SetProperty(ref _newBaseAmount, value); }

    private PartyListItemDto? _selectedParty;
    public PartyListItemDto? SelectedParty { get => _selectedParty; set => SetProperty(ref _selectedParty, value); }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var contracts = await _listHandler.HandleAsync(IncludeArchived, CancellationToken.None);
            Contracts.Clear();
            foreach (var c in contracts)
            {
                if (_tagFilteredIds != null && !_tagFilteredIds.Contains(c.ContractId)) continue;
                Contracts.Add(c);
            }
            IsEmpty = Contracts.Count == 0;

            var parties = await _partiesHandler.HandleAsync(false, CancellationToken.None);
            Parties.Clear();
            foreach (var p in parties) Parties.Add(p);

            if (_tagging != null) await _tagging.LoadSuggestionsAsync(TagSuggestions);
        }
        catch (Exception ex) { _toastService?.Error("Failed to load contracts", ex); }
        finally { IsLoading = false; }
    }

    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTitle)) { _toastService?.Error("Title required"); return; }
        if (SelectedParty == null) { _toastService?.Error("Select a party"); return; }
        if (NewBaseAmount <= 0) { _toastService?.Error("Amount must be positive"); return; }

        try
        {
            var termsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                BillingCycle = "Monthly",
                BillingInterval = 1,
                BillingDayOfMonth = 1,
                BaseAmount = NewBaseAmount,
                Category = NewType,
                AnnualEscalationPercent = 0,
                GracePeriodDays = 0
            });

            await _createHandler.HandleAsync(
                new CreateContractCommand(null, SelectedParty.PartyId, NewType, NewTitle.Trim(),
                    DateOnly.FromDateTime(DateTime.Today), null, NewCurrency, termsJson,
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Contract created");
            NewTitle = string.Empty;
            NewBaseAmount = 0;
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to create contract", ex); }
    }

    private async Task ArchiveAsync()
    {
        if (SelectedContract == null) { _toastService?.Error("Select a contract first"); return; }
        try
        {
            await _archiveHandler.HandleAsync(
                new ArchiveContractCommand(SelectedContract.ContractId, "Archived by user",
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Contract archived");
            SelectedContract = null;
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to archive contract", ex); }
    }

    private async Task GenerateBillsAsync()
    {
        if (SelectedContract == null) { _toastService?.Error("Select a contract first"); return; }
        try
        {
            var count = await _genBillsHandler.HandleAsync(
                new GenerateContractBillsCommand(SelectedContract.ContractId,
                    DateOnly.FromDateTime(DateTime.Today),
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success($"{count} bill(s) generated");
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to generate bills", ex); }
    }

    private async Task ExportAsync()
    {
        if (_exportService == null || Contracts.Count == 0) return;
        try
        {
            var headers = new[] { "Title", "Type", "Party", "Currency", "StartDate", "EndDate", "Archived" };
            var rows = Contracts.Select(c => (IReadOnlyList<string?>)new[]
            {
                c.Title, c.ContractType, c.PartyName, c.CurrencyCode,
                c.StartDate.ToString("yyyy-MM-dd"),
                c.EndDate?.ToString("yyyy-MM-dd") ?? "",
                c.IsArchived.ToString()
            }).ToList();
            await _exportService.ExportCsvAsync("Contracts", headers, rows);
        }
        catch (Exception ex) { _toastService?.Error("Export failed", ex); }
    }

    private async Task ApplyTagFilterAsync()
    {
        if (_tagging != null && !string.IsNullOrEmpty(SelectedTagFilter))
            _tagFilteredIds = await _tagging.GetEntityIdsByTagAsync(SelectedTagFilter, "Contract");
        else
            _tagFilteredIds = null;
        await LoadAsync();
    }

    public async Task LoadTagsForSelectedContractAsync()
    {
        if (_tagging != null && SelectedContract != null)
            await _tagging.LoadEntityTagsAsync(SelectedContract.ContractId, "Contract", SelectedEntityTags);
        else
            SelectedEntityTags.Clear();
    }
}
