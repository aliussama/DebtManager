using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class InvestmentAccountsViewModel : ObservableObject
{
    private readonly CreateInvestmentAccountHandler? _createHandler;
    private readonly ArchiveInvestmentAccountHandler? _archiveHandler;
    private readonly SetCostBasisModeHandler? _modeHandler;
    private readonly GetInvestmentPortfolioDashboardHandler? _dashboardHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly TaggingMixin? _tagging;

    public InvestmentAccountsViewModel(
        CreateInvestmentAccountHandler? createHandler = null,
        ArchiveInvestmentAccountHandler? archiveHandler = null,
        SetCostBasisModeHandler? modeHandler = null,
        GetInvestmentPortfolioDashboardHandler? dashboardHandler = null,
        Guid actorUserId = default,
        Guid deviceId = default,
        IToastService? toastService = null,
        TaggingMixin? tagging = null)
    {
        _createHandler = createHandler;
        _archiveHandler = archiveHandler;
        _modeHandler = modeHandler;
        _dashboardHandler = dashboardHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _tagging = tagging;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ShowCreateCommand = new RelayCommand(() => IsCreateVisible = true);
        CancelCreateCommand = new RelayCommand(CancelCreate);
        ConfirmCreateCommand = new AsyncRelayCommand(ConfirmCreateAsync, () => !string.IsNullOrWhiteSpace(NewAccountName));
        ArchiveCommand = new RelayCommand<InvestmentAccountDto>(item => _ = ArchiveAsync(item));
    }

    public ICommand RefreshCommand { get; }
    public ICommand ShowCreateCommand { get; }
    public ICommand CancelCreateCommand { get; }
    public ICommand ConfirmCreateCommand { get; }
    public ICommand ArchiveCommand { get; }

    public ObservableCollection<InvestmentAccountDto> Accounts { get; } = new();

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
    public ICommand RemoveTagCommand2 => new RelayCommand<string>(tag => _tagging?.RemoveTag(tag, SelectedEntityTags));
    public ICommand SaveTagsCommand => new AsyncRelayCommand(async () => { if (_tagging != null && _selectedInvestmentIdForTags != Guid.Empty) await _tagging.SaveTagsAsync(_selectedInvestmentIdForTags, "InvestmentAccount", SelectedEntityTags, TagSuggestions); });
    private Guid _selectedInvestmentIdForTags;

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

    private bool _isCreateVisible;
    public bool IsCreateVisible { get => _isCreateVisible; set => SetProperty(ref _isCreateVisible, value); }

    private string _newAccountName = string.Empty;
    public string NewAccountName { get => _newAccountName; set => SetProperty(ref _newAccountName, value); }

    private string _newCurrencyCode = "USD";
    public string NewCurrencyCode { get => _newCurrencyCode; set => SetProperty(ref _newCurrencyCode, value); }

    private string _newBrokerName = string.Empty;
    public string NewBrokerName { get => _newBrokerName; set => SetProperty(ref _newBrokerName, value); }

    public ObservableCollection<string> Currencies { get; } = new()
    {
        "EGP", "USD", "EUR", "GBP", "SAR", "AED"
    };

    public async Task LoadAsync()
    {
        if (_dashboardHandler == null) return;
        IsLoading = true;
        try
        {
            var dashboard = await _dashboardHandler.HandleAsync(ct: CancellationToken.None);
            Accounts.Clear();
            foreach (var a in dashboard.Accounts)
            {
                if (_tagFilteredIds != null && !_tagFilteredIds.Contains(a.AccountId)) continue;
                Accounts.Add(a);
            }
            IsEmpty = Accounts.Count == 0;

            if (_tagging != null) await _tagging.LoadSuggestionsAsync(TagSuggestions);
        }
        catch (Exception ex) { _toastService?.Error("Failed to load investment accounts", ex); }
        finally { IsLoading = false; }
    }

    private void CancelCreate()
    {
        IsCreateVisible = false;
        NewAccountName = string.Empty;
        NewCurrencyCode = "USD";
        NewBrokerName = string.Empty;
    }

    private async Task ConfirmCreateAsync()
    {
        if (_createHandler == null || string.IsNullOrWhiteSpace(NewAccountName)) return;
        try
        {
            await _createHandler.HandleAsync(
                new CreateInvestmentAccountCommand(null, NewAccountName.Trim(), NewCurrencyCode, NewBrokerName.Trim(),
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success($"Investment account '{NewAccountName.Trim()}' created");
            CancelCreate();
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to create investment account", ex); }
    }

    private async Task ArchiveAsync(InvestmentAccountDto? item)
    {
        if (_archiveHandler == null || item == null) return;
        try
        {
            await _archiveHandler.HandleAsync(
                new ArchiveInvestmentAccountCommand(item.AccountId, DateOnly.FromDateTime(DateTime.Today), "Archived by user"),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success($"Investment account '{item.Name}' archived");
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to archive account", ex); }
    }

    private async Task ApplyTagFilterAsync()
    {
        if (_tagging != null && !string.IsNullOrEmpty(SelectedTagFilter))
            _tagFilteredIds = await _tagging.GetEntityIdsByTagAsync(SelectedTagFilter, "InvestmentAccount");
        else
            _tagFilteredIds = null;
        await LoadAsync();
    }

    public async Task LoadTagsForInvestmentAccountAsync(Guid accountId)
    {
        _selectedInvestmentIdForTags = accountId;
        if (_tagging != null)
            await _tagging.LoadEntityTagsAsync(accountId, "InvestmentAccount", SelectedEntityTags);
        else
            SelectedEntityTags.Clear();
    }
}
