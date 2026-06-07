using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class CategoriesViewModel : ObservableObject
{
    private readonly GetCategoriesListHandler? _listHandler;
    private readonly CreateCategoryHandler? _createHandler;
    private readonly RenameCategoryHandler? _renameHandler;
    private readonly ArchiveCategoryHandler? _archiveHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;

    public CategoriesViewModel(
        GetCategoriesListHandler? listHandler = null,
        CreateCategoryHandler? createHandler = null,
        RenameCategoryHandler? renameHandler = null,
        ArchiveCategoryHandler? archiveHandler = null,
        Guid actorUserId = default,
        Guid deviceId = default,
        IToastService? toastService = null)
    {
        _listHandler = listHandler;
        _createHandler = createHandler;
        _renameHandler = renameHandler;
        _archiveHandler = archiveHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ShowCreateCommand = new RelayCommand(() => IsCreateVisible = true);
        CancelCreateCommand = new RelayCommand(CancelCreate);
        ConfirmCreateCommand = new AsyncRelayCommand(ConfirmCreateAsync, () => !string.IsNullOrWhiteSpace(NewCategoryName));
        ArchiveCommand = new RelayCommand<CategoryListItemDto>(item => _ = ArchiveAsync(item));

        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;

        SelectedKindFilter = "All";
    }

    public ICommand RefreshCommand { get; }
    public ICommand ShowCreateCommand { get; }
    public ICommand CancelCreateCommand { get; }
    public ICommand ConfirmCreateCommand { get; }
    public ICommand ArchiveCommand { get; }

    public ObservableCollection<CategoryListItemDto> Items { get; } = new();
    public ICollectionView ItemsView { get; }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

    private bool _isCreateVisible;
    public bool IsCreateVisible { get => _isCreateVisible; set => SetProperty(ref _isCreateVisible, value); }

    private string _newCategoryName = string.Empty;
    public string NewCategoryName { get => _newCategoryName; set => SetProperty(ref _newCategoryName, value); }

    private string _newCategoryKind = "expense";
    public string NewCategoryKind { get => _newCategoryKind; set => SetProperty(ref _newCategoryKind, value); }

    public ObservableCollection<string> KindOptions { get; } = new() { "expense", "income" };

    private string _selectedKindFilter = "All";
    public string SelectedKindFilter
    {
        get => _selectedKindFilter;
        set { if (SetProperty(ref _selectedKindFilter, value)) ItemsView.Refresh(); }
    }

    public ObservableCollection<string> KindFilterOptions { get; } = new() { "All", "expense", "income" };

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) ItemsView.Refresh(); }
    }

    public async Task LoadAsync()
    {
        if (_listHandler == null) return;
        IsLoading = true;
        try
        {
            var items = await _listHandler.HandleAsync(CancellationToken.None);
            Items.Clear();
            foreach (var item in items) Items.Add(item);
            IsEmpty = Items.Count == 0;
            ItemsView.Refresh();
        }
        catch (Exception ex) { _toastService?.Error("Failed to load categories", ex); }
        finally { IsLoading = false; }
    }

    private bool FilterItem(object obj)
    {
        if (obj is not CategoryListItemDto item) return false;
        if (SelectedKindFilter != "All" && item.Kind != SelectedKindFilter) return false;
        if (!string.IsNullOrWhiteSpace(SearchText) &&
            !item.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private void CancelCreate()
    {
        IsCreateVisible = false;
        NewCategoryName = string.Empty;
        NewCategoryKind = "expense";
    }

    private async Task ConfirmCreateAsync()
    {
        if (_createHandler == null || string.IsNullOrWhiteSpace(NewCategoryName)) return;
        try
        {
            await _createHandler.HandleAsync(
                new CreateCategoryCommand(null, NewCategoryName.Trim(), NewCategoryKind, null),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success($"Category '{NewCategoryName.Trim()}' created");
            CancelCreate();
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to create category", ex); }
    }

    private async Task ArchiveAsync(CategoryListItemDto? item)
    {
        if (_archiveHandler == null || item == null) return;
        try
        {
            await _archiveHandler.HandleAsync(
                new ArchiveCategoryCommand(item.CategoryId, "Archived by user"),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success($"Category '{item.Name}' archived");
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to archive category", ex); }
    }
}
