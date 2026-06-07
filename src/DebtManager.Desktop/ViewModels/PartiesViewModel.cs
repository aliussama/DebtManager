using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class PartiesViewModel : ObservableObject
{
    private readonly CreatePartyHandler _createHandler;
    private readonly ModifyPartyHandler _modifyHandler;
    private readonly ArchivePartyHandler _archiveHandler;
    private readonly GetPartiesListHandler _listHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;
    private readonly TaggingMixin? _tagging;

    public PartiesViewModel(
        CreatePartyHandler createHandler,
        ModifyPartyHandler modifyHandler,
        ArchivePartyHandler archiveHandler,
        GetPartiesListHandler listHandler,
        Guid actorUserId, Guid deviceId,
        IToastService? toastService = null,
        IExportService? exportService = null,
        TaggingMixin? tagging = null)
    {
        _createHandler = createHandler;
        _modifyHandler = modifyHandler;
        _archiveHandler = archiveHandler;
        _listHandler = listHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;
        _tagging = tagging;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        CreatePartyCommand = new AsyncRelayCommand(CreateAsync);
        ArchivePartyCommand = new AsyncRelayCommand(ArchiveAsync);
        ExportCsvCommand = new AsyncRelayCommand(ExportAsync);
    }

    public ICommand RefreshCommand { get; }
    public ICommand CreatePartyCommand { get; }
    public ICommand ArchivePartyCommand { get; }
    public ICommand ExportCsvCommand { get; }

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
    public ICommand SaveTagsCommand => new AsyncRelayCommand(async () => { if (_tagging != null && SelectedParty != null) await _tagging.SaveTagsAsync(SelectedParty.PartyId, "Party", SelectedEntityTags, TagSuggestions); });

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

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
    }

    private string _kindFilter = "All";
    public string KindFilter
    {
        get => _kindFilter;
        set { if (SetProperty(ref _kindFilter, value)) _ = LoadAsync(); }
    }

    private PartyListItemDto? _selectedParty;
    public PartyListItemDto? SelectedParty { get => _selectedParty; set => SetProperty(ref _selectedParty, value); }

    // Create form
    private string _newName = string.Empty;
    public string NewName { get => _newName; set => SetProperty(ref _newName, value); }

    private string _newKind = "Vendor";
    public string NewKind { get => _newKind; set => SetProperty(ref _newKind, value); }

    private string _newCurrency = "EGP";
    public string NewCurrency { get => _newCurrency; set => SetProperty(ref _newCurrency, value); }

    private List<PartyListItemDto> _allParties = new();

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _listHandler.HandleAsync(IncludeArchived, CancellationToken.None);
            _allParties = list.ToList();
            ApplyFilter();

            if (_tagging != null) await _tagging.LoadSuggestionsAsync(TagSuggestions);
        }
        catch (Exception ex) { _toastService?.Error("Failed to load parties", ex); }
        finally { IsLoading = false; }
    }

    private void ApplyFilter()
    {
        var filtered = _allParties.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(p => p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        if (KindFilter != "All")
            filtered = filtered.Where(p => p.Kind == KindFilter);
        if (_tagFilteredIds != null)
            filtered = filtered.Where(p => _tagFilteredIds.Contains(p.PartyId));

        Parties.Clear();
        foreach (var p in filtered) Parties.Add(p);
        IsEmpty = Parties.Count == 0;
    }

    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName)) { _toastService?.Error("Name required"); return; }
        try
        {
            await _createHandler.HandleAsync(
                new CreatePartyCommand(null, NewKind, NewName.Trim(), NewCurrency, null, [],
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Party created");
            NewName = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to create party", ex); }
    }

    private async Task ArchiveAsync()
    {
        if (SelectedParty == null) { _toastService?.Error("Select a party first"); return; }
        try
        {
            await _archiveHandler.HandleAsync(
                new ArchivePartyCommand(SelectedParty.PartyId, "Archived by user",
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Party archived");
            SelectedParty = null;
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to archive party", ex); }
    }

    private async Task ExportAsync()
    {
        if (_exportService == null || Parties.Count == 0) return;
        try
        {
            var headers = new[] { "Name", "Kind", "Currency", "Tags", "Archived" };
            var rows = Parties.Select(p => (IReadOnlyList<string?>)new[]
            {
                p.Name, p.Kind, p.DefaultCurrencyCode,
                string.Join(";", p.Tags), p.IsArchived.ToString()
            }).ToList();
            await _exportService.ExportCsvAsync("Parties", headers, rows);
        }
        catch (Exception ex) { _toastService?.Error("Export failed", ex); }
    }

    private async Task ApplyTagFilterAsync()
    {
        if (_tagging != null && !string.IsNullOrEmpty(SelectedTagFilter))
            _tagFilteredIds = await _tagging.GetEntityIdsByTagAsync(SelectedTagFilter, "Party");
        else
            _tagFilteredIds = null;
        await LoadAsync();
    }

    public async Task LoadTagsForSelectedPartyAsync()
    {
        if (_tagging != null && SelectedParty != null)
            await _tagging.LoadEntityTagsAsync(SelectedParty.PartyId, "Party", SelectedEntityTags);
        else
            SelectedEntityTags.Clear();
    }
}
