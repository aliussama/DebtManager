using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Forecasting;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class ScenariosViewModel : ObservableObject
{
    private readonly CreateForecastScenarioHandler? _createHandler;
    private readonly ArchiveForecastScenarioHandler? _archiveHandler;
    private readonly AddScenarioChangeHandler? _addChangeHandler;
    private readonly RemoveScenarioChangeHandler? _removeChangeHandler;
    private readonly GetScenarioListHandler? _listHandler;
    private readonly GetScenarioDetailHandler? _detailHandler;
    private readonly GetScenarioForecastHandler? _forecastHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;

    public ScenariosViewModel(
        CreateForecastScenarioHandler? createHandler = null,
        ArchiveForecastScenarioHandler? archiveHandler = null,
        AddScenarioChangeHandler? addChangeHandler = null,
        RemoveScenarioChangeHandler? removeChangeHandler = null,
        GetScenarioListHandler? listHandler = null,
        GetScenarioDetailHandler? detailHandler = null,
        GetScenarioForecastHandler? forecastHandler = null,
        Guid actorUserId = default,
        Guid deviceId = default,
        IToastService? toastService = null,
        IExportService? exportService = null)
    {
        _createHandler = createHandler;
        _archiveHandler = archiveHandler;
        _addChangeHandler = addChangeHandler;
        _removeChangeHandler = removeChangeHandler;
        _listHandler = listHandler;
        _detailHandler = detailHandler;
        _forecastHandler = forecastHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;

        CreateScenarioCommand = new AsyncRelayCommand(CreateScenarioAsync);
        ArchiveScenarioCommand = new AsyncRelayCommand(ArchiveSelectedAsync, () => SelectedScenario != null);
        AddChangeCommand = new AsyncRelayCommand(AddChangeAsync, () => SelectedScenario != null);
        RemoveChangeCommand = new RelayCommand<ScenarioChangeDto>(c => _ = RemoveChangeAsync(c));
        RunForecastCommand = new AsyncRelayCommand(RunForecastAsync, () => SelectedScenario != null);
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync, () => Comparison != null);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);

        ChangeKindOptions = new ObservableCollection<string>(Enum.GetNames<ScenarioChangeKind>());
    }

    public ICommand CreateScenarioCommand { get; }
    public ICommand ArchiveScenarioCommand { get; }
    public ICommand AddChangeCommand { get; }
    public ICommand RemoveChangeCommand { get; }
    public ICommand RunForecastCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand RefreshCommand { get; }
    public ObservableCollection<string> ChangeKindOptions { get; }

    public ObservableCollection<ScenarioListItemDto> Scenarios { get; } = new();
    public ObservableCollection<ScenarioChangeDto> Changes { get; } = new();

    private ScenarioListItemDto? _selectedScenario;
    public ScenarioListItemDto? SelectedScenario
    {
        get => _selectedScenario;
        set { SetProperty(ref _selectedScenario, value); _ = LoadDetailAsync(); }
    }

    private ScenarioForecastComparisonDto? _comparison;
    public ScenarioForecastComparisonDto? Comparison { get => _comparison; set => SetProperty(ref _comparison, value); }

    private string _newScenarioName = string.Empty;
    public string NewScenarioName { get => _newScenarioName; set => SetProperty(ref _newScenarioName, value); }

    private DateOnly _newHorizonStart = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly NewHorizonStart { get => _newHorizonStart; set => SetProperty(ref _newHorizonStart, value); }

    private DateOnly _newHorizonEnd = DateOnly.FromDateTime(DateTime.Today.AddMonths(6));
    public DateOnly NewHorizonEnd { get => _newHorizonEnd; set => SetProperty(ref _newHorizonEnd, value); }

    private string _selectedChangeKind = nameof(ScenarioChangeKind.OneTimeExpense);
    public string SelectedChangeKind { get => _selectedChangeKind; set => SetProperty(ref _selectedChangeKind, value); }

    private string _changePayloadJson = "{}";
    public string ChangePayloadJson { get => _changePayloadJson; set => SetProperty(ref _changePayloadJson, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    private string _statusMessage = string.Empty;
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    public async Task LoadAsync()
    {
        if (_listHandler == null) return;
        try
        {
            IsBusy = true;
            var list = await _listHandler.HandleAsync(CancellationToken.None);
            Scenarios.Clear();
            foreach (var s in list) Scenarios.Add(s);
        }
        catch (Exception ex) { StatusMessage = $"Failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task LoadDetailAsync()
    {
        if (_detailHandler == null || SelectedScenario == null) return;
        try
        {
            var detail = await _detailHandler.HandleAsync(SelectedScenario.ScenarioId, CancellationToken.None);
            Changes.Clear();
            if (detail != null)
                foreach (var c in detail.Changes) Changes.Add(c);
        }
        catch { }
    }

    private async Task CreateScenarioAsync()
    {
        if (_createHandler == null || string.IsNullOrWhiteSpace(NewScenarioName)) return;
        try
        {
            IsBusy = true;
            await _createHandler.HandleAsync(
                new CreateForecastScenarioCommand(null, NewScenarioName, string.Empty,
                    NewHorizonStart, NewHorizonEnd, ForecastGranularity.Monthly),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Scenario created");
            NewScenarioName = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error(ex.Message); }
        finally { IsBusy = false; }
    }

    private async Task ArchiveSelectedAsync()
    {
        if (_archiveHandler == null || SelectedScenario == null) return;
        try
        {
            await _archiveHandler.HandleAsync(
                new ArchiveForecastScenarioCommand(SelectedScenario.ScenarioId, "Archived by user"),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Scenario archived");
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error(ex.Message); }
    }

    private async Task AddChangeAsync()
    {
        if (_addChangeHandler == null || SelectedScenario == null) return;
        try
        {
            var kind = Enum.Parse<ScenarioChangeKind>(SelectedChangeKind);
            await _addChangeHandler.HandleAsync(
                new AddScenarioChangeCommand(SelectedScenario.ScenarioId, null, kind, ChangePayloadJson),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Change added");
            await LoadDetailAsync();
        }
        catch (Exception ex) { _toastService?.Error(ex.Message); }
    }

    private async Task RemoveChangeAsync(ScenarioChangeDto? change)
    {
        if (_removeChangeHandler == null || SelectedScenario == null || change == null) return;
        try
        {
            await _removeChangeHandler.HandleAsync(
                new RemoveScenarioChangeCommand(SelectedScenario.ScenarioId, change.ChangeId, "User removed"),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Change removed");
            await LoadDetailAsync();
        }
        catch (Exception ex) { _toastService?.Error(ex.Message); }
    }

    private async Task RunForecastAsync()
    {
        if (_forecastHandler == null || SelectedScenario == null) return;
        try
        {
            IsBusy = true;
            StatusMessage = "Running scenario forecast...";
            Comparison = await _forecastHandler.HandleAsync(
                new GetScenarioForecastQuery(SelectedScenario.ScenarioId), CancellationToken.None);
            StatusMessage = $"Delta end balance: {Comparison.DeltaEndBalance:N2}, Delta net cashflow: {Comparison.DeltaNetCashflow:N2}";
            _toastService?.Success("Scenario forecast computed");
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; _toastService?.Error(ex.Message); }
        finally { IsBusy = false; }
    }

    private async Task ExportCsvAsync()
    {
        if (_exportService == null || Comparison == null) return;
        try
        {
            await _exportService.ExportCustomCsvAsync("scenario_comparison", writer =>
            {
                writer.WriteLine("# Scenario Comparison Report");
                writer.WriteLine($"# Baseline End Balance: {Comparison.BaselineEndBalance}");
                writer.WriteLine($"# Scenario End Balance: {Comparison.ScenarioEndBalance}");
                writer.WriteLine($"# Delta End Balance: {Comparison.DeltaEndBalance}");
                writer.WriteLine($"# Baseline Net Cashflow: {Comparison.BaselineNetCashflow}");
                writer.WriteLine($"# Scenario Net Cashflow: {Comparison.ScenarioNetCashflow}");
                writer.WriteLine($"# Delta Net Cashflow: {Comparison.DeltaNetCashflow}");
                writer.WriteLine();
                writer.WriteLine("Section,Metric,Baseline,Scenario,Delta");
                writer.WriteLine($"Summary,EndBalance,{Comparison.BaselineEndBalance},{Comparison.ScenarioEndBalance},{Comparison.DeltaEndBalance}");
                writer.WriteLine($"Summary,NetCashflow,{Comparison.BaselineNetCashflow},{Comparison.ScenarioNetCashflow},{Comparison.DeltaNetCashflow}");
            });
            _toastService?.Success("Comparison exported");
        }
        catch (Exception ex) { _toastService?.Error(ex.Message); }
    }
}
