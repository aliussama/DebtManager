using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Documents;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class DocumentVaultViewModel : ObservableObject
{
    private readonly AddDocumentHandler _addHandler;
    private readonly UpdateDocumentMetadataHandler _updateHandler;
    private readonly ArchiveDocumentHandler _archiveHandler;
    private readonly PurgeDocumentBlobHandler _purgeHandler;
    private readonly ExportDocumentHandler _exportHandler;
    private readonly LinkDocumentHandler _linkHandler;
    private readonly UnlinkDocumentHandler _unlinkHandler;
    private readonly GetDocumentVaultDashboardHandler _dashboardHandler;
    private readonly GetDocumentsListHandler _listHandler;
    private readonly IFileDialogService? _fileDialogService;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;

    public DocumentVaultViewModel(
        AddDocumentHandler addHandler,
        UpdateDocumentMetadataHandler updateHandler,
        ArchiveDocumentHandler archiveHandler,
        PurgeDocumentBlobHandler purgeHandler,
        ExportDocumentHandler exportHandler,
        LinkDocumentHandler linkHandler,
        UnlinkDocumentHandler unlinkHandler,
        GetDocumentVaultDashboardHandler dashboardHandler,
        GetDocumentsListHandler listHandler,
        Guid actorUserId,
        Guid deviceId,
        IFileDialogService? fileDialogService = null,
        IToastService? toastService = null,
        IExportService? exportService = null)
    {
        _addHandler = addHandler;
        _updateHandler = updateHandler;
        _archiveHandler = archiveHandler;
        _purgeHandler = purgeHandler;
        _exportHandler = exportHandler;
        _linkHandler = linkHandler;
        _unlinkHandler = unlinkHandler;
        _dashboardHandler = dashboardHandler;
        _listHandler = listHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _fileDialogService = fileDialogService;
        _toastService = toastService;
        _exportService = exportService;

        AddDocumentCommand = new AsyncRelayCommand(AddDocumentAsync);
        ArchiveDocumentCommand = new AsyncRelayCommand(ArchiveDocumentAsync);
        PurgeDocumentBlobCommand = new AsyncRelayCommand(PurgeDocumentBlobAsync);
        ExportDocumentCommand = new AsyncRelayCommand(ExportDocumentAsync);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
    }

    public ObservableCollection<DocumentListItem> Documents { get; } = new();

    private DocumentVaultSummary? _summary;
    public DocumentVaultSummary? Summary
    {
        get => _summary;
        set => SetProperty(ref _summary, value);
    }

    private DocumentListItem? _selectedDocument;
    public DocumentListItem? SelectedDocument
    {
        get => _selectedDocument;
        set => SetProperty(ref _selectedDocument, value);
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    private string _tagFilter = "";
    public string TagFilter
    {
        get => _tagFilter;
        set => SetProperty(ref _tagFilter, value);
    }

    private bool _showArchivedOnly;
    public bool ShowArchivedOnly
    {
        get => _showArchivedOnly;
        set => SetProperty(ref _showArchivedOnly, value);
    }

    private bool _showMissingBlobOnly;
    public bool ShowMissingBlobOnly
    {
        get => _showMissingBlobOnly;
        set => SetProperty(ref _showMissingBlobOnly, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public ICommand AddDocumentCommand { get; }
    public ICommand ArchiveDocumentCommand { get; }
    public ICommand PurgeDocumentBlobCommand { get; }
    public ICommand ExportDocumentCommand { get; }
    public ICommand RefreshCommand { get; }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            Summary = await _dashboardHandler.HandleAsync(CancellationToken.None);

            bool? archived = ShowArchivedOnly ? true : null;
            bool? missingBlob = ShowMissingBlobOnly ? true : null;

            var docs = await _listHandler.HandleAsync(
                string.IsNullOrWhiteSpace(TagFilter) ? null : TagFilter,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                null, archived, missingBlob, CancellationToken.None);

            Documents.Clear();
            foreach (var d in docs)
                Documents.Add(d);
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to load documents", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task AddDocumentAsync()
    {
        var path = _fileDialogService?.ShowOpenFileDialog(
            "Select Document",
            "All Files (*.*)|*.*|PDF (*.pdf)|*.pdf|Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg");

        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            var bytes = await System.IO.File.ReadAllBytesAsync(path);
            var fileName = System.IO.Path.GetFileName(path);

            await _addHandler.HandleAsync(
                new AddDocumentCommand(null, fileName, null, "[]", "", DateOnly.FromDateTime(DateTime.Today), bytes),
                _actorUserId, _deviceId, CancellationToken.None);

            _toastService?.Success($"Document added: {fileName}");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to add document", ex);
        }
    }

    private async Task ArchiveDocumentAsync()
    {
        if (SelectedDocument == null) return;
        try
        {
            await _archiveHandler.HandleAsync(
                new ArchiveDocumentCommand(SelectedDocument.DocumentId, "User archived", DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Document archived");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to archive document", ex);
        }
    }

    private async Task PurgeDocumentBlobAsync()
    {
        if (SelectedDocument == null) return;
        try
        {
            await _purgeHandler.HandleAsync(
                new PurgeDocumentBlobCommand(SelectedDocument.DocumentId, "User purged local blob", DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Local blob purged");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to purge blob", ex);
        }
    }

    private async Task ExportDocumentAsync()
    {
        if (SelectedDocument == null) return;

        var savePath = _fileDialogService?.ShowSaveFileDialog(
            SelectedDocument.FileName,
            "All Files (*.*)|*.*",
            "Export Document");

        if (string.IsNullOrEmpty(savePath)) return;

        try
        {
            await _exportHandler.HandleAsync(
                new ExportDocumentCommand(SelectedDocument.DocumentId, savePath, DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success($"Document exported: {System.IO.Path.GetFileName(savePath)}");
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to export document", ex);
        }
    }
}
