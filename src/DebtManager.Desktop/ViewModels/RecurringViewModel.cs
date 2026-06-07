using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class RecurringViewModel : ObservableObject
{
    private readonly GetRecurringDashboardHandler? _dashboardHandler;
    private readonly CreateRecurringHandler? _createHandler;
    private readonly ArchiveRecurringHandler? _archiveHandler;
    private readonly PostRecurringNowHandler? _postHandler;
    private readonly GetAccountsListHandler? _accountsHandler;
    private readonly GetCategoriesListHandler? _categoriesHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;
    private readonly TaggingMixin? _tagging;

    public RecurringViewModel(
        GetRecurringDashboardHandler? dashboardHandler = null,
        CreateRecurringHandler? createHandler = null,
        ArchiveRecurringHandler? archiveHandler = null,
        PostRecurringNowHandler? postHandler = null,
        GetAccountsListHandler? accountsHandler = null,
        GetCategoriesListHandler? categoriesHandler = null,
        Guid actorUserId = default,
        Guid deviceId = default,
        IToastService? toastService = null,
        IExportService? exportService = null,
        TaggingMixin? tagging = null)
    {
        _dashboardHandler = dashboardHandler;
        _createHandler = createHandler;
        _archiveHandler = archiveHandler;
        _postHandler = postHandler;
        _accountsHandler = accountsHandler;
        _categoriesHandler = categoriesHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;
        _tagging = tagging;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ShowCreateCommand = new RelayCommand(() => IsCreateVisible = true);
        CancelCreateCommand = new RelayCommand(CancelCreate);
        ConfirmCreateCommand = new AsyncRelayCommand(ConfirmCreateAsync);
        PostNowCommand = new RelayCommand<RecurringDashboardItemDto>(item => _ = PostNowAsync(item));
        ArchiveCommand = new RelayCommand<RecurringDashboardItemDto>(item => _ = ArchiveAsync(item));
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync, () => Items.Count > 0);

        NewKind = "expense";
        NewFrequency = "Monthly";
        NewInterval = 1;
        NewCurrency = "EGP";
    }

    public ICommand RefreshCommand { get; }
    public ICommand ShowCreateCommand { get; }
    public ICommand CancelCreateCommand { get; }
    public ICommand ConfirmCreateCommand { get; }
    public ICommand PostNowCommand { get; }
    public ICommand ArchiveCommand { get; }
    public ICommand ExportCsvCommand { get; }

    public ObservableCollection<RecurringDashboardItemDto> Items { get; } = new();
    public ObservableCollection<AccountListItemDto> AvailableAccounts { get; } = new();
    public ObservableCollection<CategoryListItemDto> AvailableCategories { get; } = new();

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
    public ICommand SaveTagsCommand => new AsyncRelayCommand(async () => { if (_tagging != null && _selectedRecurringIdForTags != Guid.Empty) await _tagging.SaveTagsAsync(_selectedRecurringIdForTags, "Recurring", SelectedEntityTags, TagSuggestions); });
    private Guid _selectedRecurringIdForTags;

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

    // Create form
    private bool _isCreateVisible;
    public bool IsCreateVisible { get => _isCreateVisible; set => SetProperty(ref _isCreateVisible, value); }

    private string _newKind = "expense";
    public string NewKind { get => _newKind; set => SetProperty(ref _newKind, value); }

    public ObservableCollection<string> KindOptions { get; } = new() { "expense", "income" };

    private AccountListItemDto? _newAccount;
    public AccountListItemDto? NewAccount { get => _newAccount; set => SetProperty(ref _newAccount, value); }

    private decimal _newAmount;
    public decimal NewAmount { get => _newAmount; set => SetProperty(ref _newAmount, value); }

    private string _newCurrency = "EGP";
    public string NewCurrency { get => _newCurrency; set => SetProperty(ref _newCurrency, value); }

    public ObservableCollection<string> Currencies { get; } = new() { "EGP", "USD", "EUR" };

    private string _newReference = string.Empty;
    public string NewReference { get => _newReference; set => SetProperty(ref _newReference, value); }

    private string _newFrequency = "Monthly";
    public string NewFrequency { get => _newFrequency; set => SetProperty(ref _newFrequency, value); }

    public ObservableCollection<string> FrequencyOptions { get; } = new() { "Weekly", "Monthly", "Quarterly", "Yearly" };

    private int _newInterval = 1;
    public int NewInterval { get => _newInterval; set => SetProperty(ref _newInterval, value); }

    private DateTime _newStartDate = DateTime.Today;
    public DateTime NewStartDate { get => _newStartDate; set => SetProperty(ref _newStartDate, value); }

    public async Task LoadAsync()
    {
        if (_dashboardHandler == null) return;
        IsLoading = true;
        try
        {
            var result = await _dashboardHandler.HandleAsync(
                DateOnly.FromDateTime(DateTime.Today), null, CancellationToken.None);
            Items.Clear();
            foreach (var item in result.Items)
            {
                if (_tagFilteredIds != null && !_tagFilteredIds.Contains(item.RecurringId)) continue;
                Items.Add(item);
            }
            IsEmpty = Items.Count == 0;

            await LoadDropdownsAsync();

            if (_tagging != null) await _tagging.LoadSuggestionsAsync(TagSuggestions);
        }
        catch (Exception ex) { _toastService?.Error("Failed to load recurring transactions", ex); }
        finally { IsLoading = false; }
    }

    private async Task LoadDropdownsAsync()
    {
        if (_accountsHandler != null)
        {
            var accts = await _accountsHandler.HandleAsync(CancellationToken.None);
            AvailableAccounts.Clear();
            foreach (var a in accts.Where(a => !a.IsArchived)) AvailableAccounts.Add(a);
        }
        if (_categoriesHandler != null)
        {
            var cats = await _categoriesHandler.HandleAsync(CancellationToken.None);
            AvailableCategories.Clear();
            foreach (var c in cats.Where(c => !c.IsArchived)) AvailableCategories.Add(c);
        }
    }

    private void CancelCreate()
    {
        IsCreateVisible = false;
        NewKind = "expense";
        NewAmount = 0;
        NewReference = string.Empty;
        NewFrequency = "Monthly";
        NewInterval = 1;
        NewAccount = null;
        NewStartDate = DateTime.Today;
    }

    private async Task ConfirmCreateAsync()
    {
        if (_createHandler == null || NewAccount == null || NewAmount <= 0) return;
        try
        {
            await _createHandler.HandleAsync(
                new CreateRecurringCommand(null, NewKind, NewAccount.AccountId,
                    NewAmount, NewCurrency, null, null, NewReference,
                    NewFrequency, NewInterval, DateOnly.FromDateTime(NewStartDate), null, false),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Recurring transaction created");
            CancelCreate();
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to create recurring transaction", ex); }
    }

    private async Task PostNowAsync(RecurringDashboardItemDto? item)
    {
        if (_postHandler == null || item == null) return;
        try
        {
            await _postHandler.HandleAsync(
                new PostRecurringNowCommand(item.RecurringId, DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Recurring transaction posted");
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error(ex.Message); }
    }

    private async Task ArchiveAsync(RecurringDashboardItemDto? item)
    {
        if (_archiveHandler == null || item == null) return;
        try
        {
            await _archiveHandler.HandleAsync(
                new ArchiveRecurringCommand(item.RecurringId, "Archived by user"),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Recurring transaction archived");
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to archive", ex); }
    }

    private async Task ExportCsvAsync()
    {
        if (_exportService == null || Items.Count == 0) return;
        try
        {
            var headers = new[] { "Kind", "Amount", "Currency", "Reference", "Frequency", "Start", "End", "Next Due", "Status" };
            var rows = Items.Select(i => (IReadOnlyList<string?>)new[]
            {
                i.Kind, i.Amount.ToString("F2"), i.CurrencyCode,
                i.Reference ?? "", i.Frequency, i.StartDate.ToString("yyyy-MM-dd"),
                i.EndDate?.ToString("yyyy-MM-dd") ?? "",
                i.NextDueDate?.ToString("yyyy-MM-dd") ?? "", i.Status
            }).ToList();
            await _exportService.ExportCsvAsync("RecurringTransactions", headers, rows);
        }
        catch (Exception ex) { _toastService?.Error("Export failed", ex); }
    }

    private async Task ApplyTagFilterAsync()
    {
        if (_tagging != null && !string.IsNullOrEmpty(SelectedTagFilter))
            _tagFilteredIds = await _tagging.GetEntityIdsByTagAsync(SelectedTagFilter, "Recurring");
        else
            _tagFilteredIds = null;
        await LoadAsync();
    }

    public async Task LoadTagsForRecurringAsync(Guid recurringId)
    {
        _selectedRecurringIdForTags = recurringId;
        if (_tagging != null)
            await _tagging.LoadEntityTagsAsync(recurringId, "Recurring", SelectedEntityTags);
        else
            SelectedEntityTags.Clear();
    }
}
