using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Events;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class IncomeSourcesViewModel : ObservableObject
{
    private readonly DefineIncomeSourceHandler _defineHandler;
    private readonly ArchiveIncomeSourceHandler _archiveHandler;
    private readonly GetIncomeSourcesHandler _listHandler;
    private readonly GetIncomeBySourceReportHandler _reportHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;

    public IncomeSourcesViewModel(
        DefineIncomeSourceHandler defineHandler,
        ArchiveIncomeSourceHandler archiveHandler,
        GetIncomeSourcesHandler listHandler,
        GetIncomeBySourceReportHandler reportHandler,
        Guid actorUserId, Guid deviceId,
        IToastService? toastService = null,
        IExportService? exportService = null)
    {
        _defineHandler = defineHandler;
        _archiveHandler = archiveHandler;
        _listHandler = listHandler;
        _reportHandler = reportHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        CreateCommand = new AsyncRelayCommand(CreateAsync);
        ArchiveCommand = new AsyncRelayCommand(async (param) =>
        {
            if (param is IncomeSourceDto dto)
                await ArchiveAsync(dto);
        });
        ExportCsvCommand = new AsyncRelayCommand(ExportAsync, () => Sources.Count > 0);
        ShowCreateCommand = new RelayCommand(() => IsCreateVisible = true);
        CancelCreateCommand = new RelayCommand(CancelCreate);
    }

    public ICommand RefreshCommand { get; }
    public ICommand CreateCommand { get; }
    public ICommand ArchiveCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ShowCreateCommand { get; }
    public ICommand CancelCreateCommand { get; }

    public ObservableCollection<IncomeSourceDto> Sources { get; } = new();

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

    private bool _isCreateVisible;
    public bool IsCreateVisible { get => _isCreateVisible; set => SetProperty(ref _isCreateVisible, value); }

    // Create form fields
    private string _newName = string.Empty;
    public string NewName { get => _newName; set => SetProperty(ref _newName, value); }

    private string _newSourceType = "Salary";
    public string NewSourceType { get => _newSourceType; set => SetProperty(ref _newSourceType, value); }

    private string _newCurrency = "EGP";
    public string NewCurrency { get => _newCurrency; set => SetProperty(ref _newCurrency, value); }

    private bool _newIsRecurring = true;
    public bool NewIsRecurring { get => _newIsRecurring; set => SetProperty(ref _newIsRecurring, value); }

    private string _newNotes = string.Empty;
    public string NewNotes { get => _newNotes; set => SetProperty(ref _newNotes, value); }

    public IReadOnlyList<string> SourceTypeOptions { get; } =
        Enum.GetNames<IncomeSourceType>().ToList();

    // Report summary
    private decimal _totalClassified;
    public decimal TotalClassified { get => _totalClassified; set => SetProperty(ref _totalClassified, value); }

    private decimal _totalUnclassified;
    public decimal TotalUnclassified { get => _totalUnclassified; set => SetProperty(ref _totalUnclassified, value); }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _listHandler.HandleAsync(CancellationToken.None);
            Sources.Clear();
            foreach (var s in list)
                Sources.Add(s);
            IsEmpty = Sources.Count == 0;

            // Load report summary (last 12 months)
            var to = DateOnly.FromDateTime(DateTime.Today);
            var from = to.AddMonths(-12);
            var report = await _reportHandler.HandleAsync(from, to, null, CancellationToken.None);
            TotalClassified = report.PerSourceTotals.Sum(r => r.Total);
            TotalUnclassified = report.Unclassified.Total;
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to load income sources", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task CreateAsync()
    {
        try
        {
            if (!Enum.TryParse<IncomeSourceType>(NewSourceType, out var sourceType))
                sourceType = IncomeSourceType.Other;

            await _defineHandler.HandleAsync(
                new DefineIncomeSourceCommand(
                    NewName,
                    sourceType,
                    NewCurrency,
                    NewIsRecurring,
                    DateOnly.FromDateTime(DateTime.Today),
                    string.IsNullOrWhiteSpace(NewNotes) ? null : NewNotes),
                _actorUserId, _deviceId, CancellationToken.None);

            _toastService?.Success("Income source created");
            CancelCreate();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to create income source", ex);
        }
    }

    private async Task ArchiveAsync(IncomeSourceDto? source)
    {
        if (source == null || source.IsArchived) return;

        try
        {
            await _archiveHandler.HandleAsync(
                new ArchiveIncomeSourceCommand(source.SourceId, "Archived by user", DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);

            _toastService?.Success($"Income source '{source.Name}' archived");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to archive income source", ex);
        }
    }

    private void CancelCreate()
    {
        IsCreateVisible = false;
        NewName = string.Empty;
        NewSourceType = "Salary";
        NewCurrency = "EGP";
        NewIsRecurring = true;
        NewNotes = string.Empty;
    }

    private async Task ExportAsync()
    {
        if (_exportService == null || Sources.Count == 0) return;

        try
        {
            var headers = new[] { "Name", "Type", "Currency", "Recurring", "Archived", "Total Received", "Last Received", "Notes" };
            var rows = Sources.Select(s => (IReadOnlyList<string?>)new[]
            {
                s.Name,
                s.SourceType,
                s.CurrencyCode,
                s.IsRecurring ? "Yes" : "No",
                s.IsArchived ? "Yes" : "No",
                s.TotalReceived.ToString("F2"),
                s.LastReceivedDate?.ToString("yyyy-MM-dd") ?? "",
                s.Notes ?? ""
            }).ToList();

            await _exportService.ExportCsvAsync("IncomeSources", headers, rows);
            _toastService?.Success("Income sources exported");
        }
        catch (Exception ex)
        {
            _toastService?.Error("Export failed", ex);
        }
    }
}
