using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class AssetsViewModel : ObservableObject
{
    private readonly GetAssetsListHandler? _listHandler;
    private readonly CreateAssetHandler? _createHandler;
    private readonly ArchiveAssetHandler? _archiveHandler;
    private readonly RecordAssetPriceHandler? _priceHandler;
    private readonly AdjustAssetQuantityHandler? _adjustHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly TaggingMixin? _tagging;

    public AssetsViewModel(
        GetAssetsListHandler? listHandler = null,
        CreateAssetHandler? createHandler = null,
        ArchiveAssetHandler? archiveHandler = null,
        RecordAssetPriceHandler? priceHandler = null,
        AdjustAssetQuantityHandler? adjustHandler = null,
        Guid actorUserId = default,
        Guid deviceId = default,
        IToastService? toastService = null,
        TaggingMixin? tagging = null)
    {
        _listHandler = listHandler;
        _createHandler = createHandler;
        _archiveHandler = archiveHandler;
        _priceHandler = priceHandler;
        _adjustHandler = adjustHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _tagging = tagging;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ShowCreateCommand = new RelayCommand(() => IsCreateVisible = true);
        CancelCreateCommand = new RelayCommand(CancelCreate);
        ConfirmCreateCommand = new AsyncRelayCommand(ConfirmCreateAsync, () => !string.IsNullOrWhiteSpace(NewAssetName));
        ArchiveCommand = new RelayCommand<AssetListItemDto>(item => _ = ArchiveAsync(item));
        RecordPriceCommand = new RelayCommand<AssetListItemDto>(item => ShowPriceForm(item));
        CancelPriceCommand = new RelayCommand(CancelPrice);
        ConfirmPriceCommand = new AsyncRelayCommand(ConfirmPriceAsync);
        AdjustQuantityCommand = new RelayCommand<AssetListItemDto>(item => ShowAdjustForm(item));
        CancelAdjustCommand = new RelayCommand(CancelAdjust);
        ConfirmAdjustCommand = new AsyncRelayCommand(ConfirmAdjustAsync);

        NewAssetType = "RealEstate";
        NewCurrencyCode = "EGP";
    }

    public ICommand RefreshCommand { get; }
    public ICommand ShowCreateCommand { get; }
    public ICommand CancelCreateCommand { get; }
    public ICommand ConfirmCreateCommand { get; }
    public ICommand ArchiveCommand { get; }
    public ICommand RecordPriceCommand { get; }
    public ICommand CancelPriceCommand { get; }
    public ICommand ConfirmPriceCommand { get; }
    public ICommand AdjustQuantityCommand { get; }
    public ICommand CancelAdjustCommand { get; }
    public ICommand ConfirmAdjustCommand { get; }

    public ObservableCollection<AssetListItemDto> Assets { get; } = new();

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
    private string _newTagText2 = string.Empty;
    public string NewTagText2 { get => _newTagText2; set => SetProperty(ref _newTagText2, value); }
    public ICommand AddTagCommand => new RelayCommand(() => { _tagging?.AddTag(NewTagText2, SelectedEntityTags); NewTagText2 = string.Empty; });
    public ICommand RemoveTagCommand2 => new RelayCommand<string>(tag => _tagging?.RemoveTag(tag, SelectedEntityTags));
    public ICommand SaveTagsCommand => new AsyncRelayCommand(async () => { if (_tagging != null && _selectedAssetForTags != Guid.Empty) await _tagging.SaveTagsAsync(_selectedAssetForTags, "Asset", SelectedEntityTags, TagSuggestions); });
    private Guid _selectedAssetForTags;

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

    // Create form
    private bool _isCreateVisible;
    public bool IsCreateVisible { get => _isCreateVisible; set => SetProperty(ref _isCreateVisible, value); }

    private string _newAssetName = string.Empty;
    public string NewAssetName { get => _newAssetName; set => SetProperty(ref _newAssetName, value); }

    private string _newAssetType = "RealEstate";
    public string NewAssetType { get => _newAssetType; set => SetProperty(ref _newAssetType, value); }

    private string _newCurrencyCode = "EGP";
    public string NewCurrencyCode { get => _newCurrencyCode; set => SetProperty(ref _newCurrencyCode, value); }

    private decimal _newQuantity = 1m;
    public decimal NewQuantity { get => _newQuantity; set => SetProperty(ref _newQuantity, value); }

    private string _newQuantityUnit = "property";
    public string NewQuantityUnit { get => _newQuantityUnit; set => SetProperty(ref _newQuantityUnit, value); }

    private string _newNotes = string.Empty;
    public string NewNotes { get => _newNotes; set => SetProperty(ref _newNotes, value); }

    public ObservableCollection<string> AssetTypes { get; } = new()
    {
        "RealEstate", "Vehicle", "PreciousMetal", "SecurityHolding", "CashEquivalent", "Custom"
    };

    public ObservableCollection<string> Currencies { get; } = new()
    {
        "EGP", "USD", "EUR", "GBP", "SAR", "AED"
    };

    // Price form
    private bool _isPriceFormVisible;
    public bool IsPriceFormVisible { get => _isPriceFormVisible; set => SetProperty(ref _isPriceFormVisible, value); }

    private Guid _priceAssetId;
    private string _priceAssetName = string.Empty;
    public string PriceAssetName { get => _priceAssetName; set => SetProperty(ref _priceAssetName, value); }

    private decimal _priceAmount;
    public decimal PriceAmount { get => _priceAmount; set => SetProperty(ref _priceAmount, value); }

    private string _priceCurrencyCode = "EGP";
    public string PriceCurrencyCode { get => _priceCurrencyCode; set => SetProperty(ref _priceCurrencyCode, value); }

    private string _priceSource = "Manual";
    public string PriceSource { get => _priceSource; set => SetProperty(ref _priceSource, value); }

    // Adjust form
    private bool _isAdjustFormVisible;
    public bool IsAdjustFormVisible { get => _isAdjustFormVisible; set => SetProperty(ref _isAdjustFormVisible, value); }

    private Guid _adjustAssetId;
    private string _adjustAssetName = string.Empty;
    public string AdjustAssetName { get => _adjustAssetName; set => SetProperty(ref _adjustAssetName, value); }

    private decimal _adjustDelta;
    public decimal AdjustDelta { get => _adjustDelta; set => SetProperty(ref _adjustDelta, value); }

    private string _adjustReason = string.Empty;
    public string AdjustReason { get => _adjustReason; set => SetProperty(ref _adjustReason, value); }

    public async Task LoadAsync()
    {
        if (_listHandler == null) return;
        IsLoading = true;
        try
        {
            var items = await _listHandler.HandleAsync(ct: CancellationToken.None);
            Assets.Clear();
            foreach (var item in items)
            {
                if (_tagFilteredIds != null && !_tagFilteredIds.Contains(item.AssetId)) continue;
                Assets.Add(item);
            }
            IsEmpty = Assets.Count == 0;

            if (_tagging != null) await _tagging.LoadSuggestionsAsync(TagSuggestions);
        }
        catch (Exception ex) { _toastService?.Error("Failed to load assets", ex); }
        finally { IsLoading = false; }
    }

    private void CancelCreate()
    {
        IsCreateVisible = false;
        NewAssetName = string.Empty;
        NewAssetType = "RealEstate";
        NewCurrencyCode = "EGP";
        NewQuantity = 1m;
        NewQuantityUnit = "property";
        NewNotes = string.Empty;
    }

    private async Task ConfirmCreateAsync()
    {
        if (_createHandler == null || string.IsNullOrWhiteSpace(NewAssetName)) return;
        try
        {
            var qtySpec = JsonSerializer.Serialize(new { unit = NewQuantityUnit, amount = NewQuantity });
            await _createHandler.HandleAsync(
                new CreateAssetCommand(null, NewAssetName.Trim(), NewAssetType, NewCurrencyCode,
                    qtySpec, [], NewNotes, DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success($"Asset '{NewAssetName.Trim()}' created");
            CancelCreate();
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to create asset", ex); }
    }

    private async Task ArchiveAsync(AssetListItemDto? item)
    {
        if (_archiveHandler == null || item == null) return;
        try
        {
            await _archiveHandler.HandleAsync(
                new ArchiveAssetCommand(item.AssetId, DateOnly.FromDateTime(DateTime.Today), "Archived by user"),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success($"Asset '{item.Name}' archived");
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to archive asset", ex); }
    }

    private void ShowPriceForm(AssetListItemDto? item)
    {
        if (item == null) return;
        _priceAssetId = item.AssetId;
        PriceAssetName = item.Name;
        PriceAmount = 0;
        PriceCurrencyCode = item.NativeCurrencyCode;
        PriceSource = "Manual";
        IsPriceFormVisible = true;
    }

    private void CancelPrice()
    {
        IsPriceFormVisible = false;
        PriceAmount = 0;
    }

    private async Task ConfirmPriceAsync()
    {
        if (_priceHandler == null) return;
        try
        {
            await _priceHandler.HandleAsync(
                new RecordAssetPriceCommand(null, _priceAssetId, DateOnly.FromDateTime(DateTime.Today),
                    PriceAmount, PriceCurrencyCode, PriceSource, string.Empty),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Price recorded");
            CancelPrice();
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to record price", ex); }
    }

    private void ShowAdjustForm(AssetListItemDto? item)
    {
        if (item == null) return;
        _adjustAssetId = item.AssetId;
        AdjustAssetName = item.Name;
        AdjustDelta = 0;
        AdjustReason = string.Empty;
        IsAdjustFormVisible = true;
    }

    private void CancelAdjust()
    {
        IsAdjustFormVisible = false;
        AdjustDelta = 0;
        AdjustReason = string.Empty;
    }

    private async Task ConfirmAdjustAsync()
    {
        if (_adjustHandler == null) return;
        try
        {
            var deltaSpec = JsonSerializer.Serialize(new { unit = "units", amount = AdjustDelta });
            await _adjustHandler.HandleAsync(
                new AdjustAssetQuantityCommand(null, _adjustAssetId, deltaSpec,
                    DateOnly.FromDateTime(DateTime.Today), AdjustReason),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Quantity adjusted");
            CancelAdjust();
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to adjust quantity", ex); }
    }

    private async Task ApplyTagFilterAsync()
    {
        if (_tagging != null && !string.IsNullOrEmpty(SelectedTagFilter))
            _tagFilteredIds = await _tagging.GetEntityIdsByTagAsync(SelectedTagFilter, "Asset");
        else
            _tagFilteredIds = null;
        await LoadAsync();
    }

    public async Task LoadTagsForAssetAsync(Guid assetId)
    {
        _selectedAssetForTags = assetId;
        if (_tagging != null)
            await _tagging.LoadEntityTagsAsync(assetId, "Asset", SelectedEntityTags);
        else
            SelectedEntityTags.Clear();
    }
}
