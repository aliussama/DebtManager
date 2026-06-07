using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class AttachmentsPanelViewModel : ObservableObject
{
    private readonly AddDocumentHandler _addHandler;
    private readonly LinkDocumentHandler _linkHandler;
    private readonly UnlinkDocumentHandler _unlinkHandler;
    private readonly ExportDocumentHandler _exportHandler;
    private readonly GetDocumentsForEntityHandler _forEntityHandler;
    private readonly IFileDialogService? _fileDialogService;
    private readonly IToastService? _toastService;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;

    private string _entityType = "";
    private string _entityId = "";

    public AttachmentsPanelViewModel(
        AddDocumentHandler addHandler,
        LinkDocumentHandler linkHandler,
        UnlinkDocumentHandler unlinkHandler,
        ExportDocumentHandler exportHandler,
        GetDocumentsForEntityHandler forEntityHandler,
        Guid actorUserId,
        Guid deviceId,
        IFileDialogService? fileDialogService = null,
        IToastService? toastService = null)
    {
        _addHandler = addHandler;
        _linkHandler = linkHandler;
        _unlinkHandler = unlinkHandler;
        _exportHandler = exportHandler;
        _forEntityHandler = forEntityHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _fileDialogService = fileDialogService;
        _toastService = toastService;

        AddAttachmentCommand = new AsyncRelayCommand(AddAttachmentAsync);
        ExportAttachmentCommand = new AsyncRelayCommand(ExportAttachmentAsync);
        UnlinkAttachmentCommand = new AsyncRelayCommand(UnlinkAttachmentAsync);
    }

    public ObservableCollection<DocumentsForEntityItem> Attachments { get; } = new();

    private DocumentsForEntityItem? _selectedAttachment;
    public DocumentsForEntityItem? SelectedAttachment
    {
        get => _selectedAttachment;
        set => SetProperty(ref _selectedAttachment, value);
    }

    public bool HasNoAttachments => Attachments.Count == 0;

    public ICommand AddAttachmentCommand { get; }
    public ICommand ExportAttachmentCommand { get; }
    public ICommand UnlinkAttachmentCommand { get; }

    public void SetEntity(string entityType, string entityId)
    {
        _entityType = entityType;
        _entityId = entityId;
    }

    public async Task LoadAsync()
    {
        if (string.IsNullOrEmpty(_entityType) || string.IsNullOrEmpty(_entityId))
            return;

        try
        {
            var items = await _forEntityHandler.HandleAsync(_entityType, _entityId, CancellationToken.None);
            Attachments.Clear();
            foreach (var item in items)
                Attachments.Add(item);
            OnPropertyChanged(nameof(HasNoAttachments));
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to load attachments", ex);
        }
    }

    private async Task AddAttachmentAsync()
    {
        var path = _fileDialogService?.ShowOpenFileDialog(
            "Select Document",
            "All Files (*.*)|*.*|PDF (*.pdf)|*.pdf|Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg");

        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var bytes = await System.IO.File.ReadAllBytesAsync(path);
            var fileName = System.IO.Path.GetFileName(path);

            var docId = await _addHandler.HandleAsync(
                new AddDocumentCommand(null, fileName, null, "[]", "", DateOnly.FromDateTime(DateTime.Today), bytes),
                _actorUserId, _deviceId, CancellationToken.None);

            await _linkHandler.HandleAsync(
                new LinkDocumentCommand(docId, _entityType, _entityId, "Receipt", DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);

            _toastService?.Success($"Attached: {fileName}");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to add attachment", ex);
        }
    }

    private async Task ExportAttachmentAsync()
    {
        if (SelectedAttachment == null) return;

        var savePath = _fileDialogService?.ShowSaveFileDialog(
            SelectedAttachment.FileName, "All Files (*.*)|*.*", "Export Attachment");
        if (string.IsNullOrEmpty(savePath)) return;

        try
        {
            await _exportHandler.HandleAsync(
                new ExportDocumentCommand(SelectedAttachment.DocumentId, savePath, DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success($"Exported: {System.IO.Path.GetFileName(savePath)}");
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to export", ex);
        }
    }

    private async Task UnlinkAttachmentAsync()
    {
        if (SelectedAttachment == null) return;

        try
        {
            await _unlinkHandler.HandleAsync(
                new UnlinkDocumentCommand(SelectedAttachment.DocumentId, _entityType, _entityId,
                    "User unlinked", DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Attachment unlinked");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to unlink", ex);
        }
    }
}
