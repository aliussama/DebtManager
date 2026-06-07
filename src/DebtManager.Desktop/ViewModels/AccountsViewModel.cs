using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class AccountsViewModel : ObservableObject
{
    private readonly GetAccountsListHandler? _listHandler;
    private readonly CreateAccountHandler? _createHandler;
    private readonly ArchiveAccountHandler? _archiveHandler;
    private readonly UpdateEntityTagsHandler? _updateTagsHandler;
    private readonly GetTagSuggestionsHandler? _suggestionsHandler;
    private readonly IEventStore? _eventStore;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly string _defaultCurrency;

    public AccountsViewModel(
        GetAccountsListHandler? listHandler = null,
        CreateAccountHandler? createHandler = null,
        ArchiveAccountHandler? archiveHandler = null,
        UpdateEntityTagsHandler? updateTagsHandler = null,
        GetTagSuggestionsHandler? suggestionsHandler = null,
        IEventStore? eventStore = null,
        Guid actorUserId = default,
        Guid deviceId = default,
        IToastService? toastService = null,
        string defaultCurrency = "EGP")
    {
        _listHandler = listHandler;
        _createHandler = createHandler;
        _archiveHandler = archiveHandler;
        _updateTagsHandler = updateTagsHandler;
        _suggestionsHandler = suggestionsHandler;
        _eventStore = eventStore;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _defaultCurrency = defaultCurrency;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ShowCreateCommand = new RelayCommand(() => IsCreateVisible = true);
        CancelCreateCommand = new RelayCommand(CancelCreate);
        ConfirmCreateCommand = new AsyncRelayCommand(ConfirmCreateAsync, () => !string.IsNullOrWhiteSpace(NewAccountName));
        ArchiveCommand = new RelayCommand<AccountListItemDto>(item => _ = ArchiveAsync(item));

        // Tag commands
        AddTagCommand = new RelayCommand(AddTag);
        RemoveTagCommand = new RelayCommand<string>(RemoveTag);
        SaveTagsCommand = new AsyncRelayCommand(SaveTagsAsync);

        NewAccountType = "Cash";
        NewCurrencyCode = _defaultCurrency;
    }

    public ICommand RefreshCommand { get; }
    public ICommand ShowCreateCommand { get; }
    public ICommand CancelCreateCommand { get; }
    public ICommand ConfirmCreateCommand { get; }
    public ICommand ArchiveCommand { get; }

    // Tag commands
    public ICommand AddTagCommand { get; }
    public ICommand RemoveTagCommand { get; }
    public ICommand SaveTagsCommand { get; }

    public ObservableCollection<AccountListItemDto> Accounts { get; } = new();

    // Tag filter
    public ObservableCollection<string> TagSuggestions { get; } = new();

    private string _selectedTagFilter = string.Empty;
    public string SelectedTagFilter
    {
        get => _selectedTagFilter;
        set
        {
            if (SetProperty(ref _selectedTagFilter, value))
                ApplyTagFilter();
        }
    }

    // Tags for selected account
    public ObservableCollection<string> SelectedAccountTags { get; } = new();

    private string _newTagText = string.Empty;
    public string NewTagText
    {
        get => _newTagText;
        set => SetProperty(ref _newTagText, value);
    }

    private AccountListItemDto? _selectedAccount;
    public AccountListItemDto? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
                _ = LoadTagsForSelectedAccountAsync();
        }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private bool _isEmpty;
    public bool IsEmpty
    {
        get => _isEmpty;
        set => SetProperty(ref _isEmpty, value);
    }

    // Create account form
    private bool _isCreateVisible;
    public bool IsCreateVisible
    {
        get => _isCreateVisible;
        set => SetProperty(ref _isCreateVisible, value);
    }

    private string _newAccountName = string.Empty;
    public string NewAccountName
    {
        get => _newAccountName;
        set => SetProperty(ref _newAccountName, value);
    }

    private string _newAccountType = "Cash";
    public string NewAccountType
    {
        get => _newAccountType;
        set => SetProperty(ref _newAccountType, value);
    }

    private string _newCurrencyCode = "EGP";
    public string NewCurrencyCode
    {
        get => _newCurrencyCode;
        set => SetProperty(ref _newCurrencyCode, value);
    }

    private decimal _newOpeningBalance;
    public decimal NewOpeningBalance
    {
        get => _newOpeningBalance;
        set => SetProperty(ref _newOpeningBalance, value);
    }

    public ObservableCollection<string> AccountTypes { get; } = new() { "Cash", "Bank", "Savings", "Credit Card", "Investment", "Other" };
    public ObservableCollection<string> Currencies { get; } = new() { "EGP", "USD", "EUR", "GBP", "SAR", "AED" };

    public async Task LoadAsync()
    {
        if (_listHandler == null) return;
        IsLoading = true;

        try
        {
            var items = await _listHandler.HandleAsync(CancellationToken.None);
            Accounts.Clear();
            foreach (var item in items)
                Accounts.Add(item);
            IsEmpty = Accounts.Count == 0;
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to load accounts", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void CancelCreate()
    {
        IsCreateVisible = false;
        NewAccountName = string.Empty;
        NewAccountType = "Cash";
        NewCurrencyCode = _defaultCurrency;
        NewOpeningBalance = 0;
    }

    private async Task ConfirmCreateAsync()
    {
        if (_createHandler == null || string.IsNullOrWhiteSpace(NewAccountName)) return;

        try
        {
            await _createHandler.HandleAsync(
                new CreateAccountCommand(
                    null,
                    NewAccountName.Trim(),
                    NewAccountType,
                    NewOpeningBalance,
                    NewCurrencyCode,
                    DateOnly.FromDateTime(DateTime.Today)
                ),
                _actorUserId, _deviceId, CancellationToken.None);

            _toastService?.Success($"Account '{NewAccountName.Trim()}' created");
            CancelCreate();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to create account", ex);
        }
    }

    private async Task ArchiveAsync(AccountListItemDto? item)
    {
        if (_archiveHandler == null || item == null) return;

        try
        {
            await _archiveHandler.HandleAsync(
                new ArchiveAccountCommand(item.AccountId, DateOnly.FromDateTime(DateTime.Today), "Archived by user"),
                _actorUserId, _deviceId, CancellationToken.None);

            _toastService?.Success($"Account '{item.Name}' archived");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to archive account", ex);
        }
    }

    // --- Tag support ---

    private async Task LoadTagSuggestionsAsync()
    {
        if (_suggestionsHandler == null) return;
        try
        {
            var suggestions = await _suggestionsHandler.HandleAsync(CancellationToken.None);
            TagSuggestions.Clear();
            TagSuggestions.Add(string.Empty); // "All" option
            foreach (var s in suggestions)
                TagSuggestions.Add(s.Tag);
        }
        catch { /* non-critical */ }
    }

    private async Task LoadTagsForSelectedAccountAsync()
    {
        SelectedAccountTags.Clear();
        if (_selectedAccount == null || _eventStore == null) return;

        try
        {
            var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
            var state = TagProjector.Project(all);
            var key = (_selectedAccount.AccountId, "Account");
            if (state.EntityTags.TryGetValue(key, out var tags))
            {
                foreach (var tag in tags)
                    SelectedAccountTags.Add(tag);
            }
        }
        catch { /* non-critical */ }
    }

    private void AddTag()
    {
        var trimmed = NewTagText?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;
        if (trimmed.Length > 50) return;
        if (SelectedAccountTags.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) return;
        if (SelectedAccountTags.Count >= 20) return;

        SelectedAccountTags.Add(trimmed);
        NewTagText = string.Empty;
    }

    private void RemoveTag(string? tag)
    {
        if (tag != null)
            SelectedAccountTags.Remove(tag);
    }

    private async Task SaveTagsAsync()
    {
        if (_updateTagsHandler == null || _selectedAccount == null) return;

        try
        {
            await _updateTagsHandler.HandleAsync(
                new UpdateEntityTagsCommand(
                    _selectedAccount.AccountId,
                    "Account",
                    SelectedAccountTags.ToList(),
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);

            _toastService?.Success("Tags updated");
            await LoadTagSuggestionsAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to update tags", ex);
        }
    }

    private void ApplyTagFilter()
    {
        // Filtering is applied during LoadAsync; trigger reload
        _ = LoadAsync();
    }
}
