using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Forecasting;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class ForecastViewModel : ObservableObject
{
    private readonly GetBaselineForecastHandler? _baselineHandler;
    private readonly GetForecastDashboardHandler? _dashboardHandler;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;

    public ForecastViewModel(
        GetBaselineForecastHandler? baselineHandler = null,
        GetForecastDashboardHandler? dashboardHandler = null,
        IToastService? toastService = null,
        IExportService? exportService = null)
    {
        _baselineHandler = baselineHandler;
        _dashboardHandler = dashboardHandler;
        _toastService = toastService;
        _exportService = exportService;

        RefreshBaselineCommand = new AsyncRelayCommand(RefreshBaselineAsync);
        ExportBaselineCsvCommand = new AsyncRelayCommand(ExportBaselineCsvAsync, () => Report != null);

        GranularityOptions = new ObservableCollection<string>(Enum.GetNames<ForecastGranularity>());
        SelectedGranularity = nameof(ForecastGranularity.Monthly);
        StartDate = DateOnly.FromDateTime(DateTime.Today);
        EndDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(6));
    }

    public ICommand RefreshBaselineCommand { get; }
    public ICommand ExportBaselineCsvCommand { get; }
    public ObservableCollection<string> GranularityOptions { get; }

    private DateOnly _startDate;
    public DateOnly StartDate { get => _startDate; set => SetProperty(ref _startDate, value); }

    private DateOnly _endDate;
    public DateOnly EndDate { get => _endDate; set => SetProperty(ref _endDate, value); }

    private string _selectedGranularity = "Monthly";
    public string SelectedGranularity { get => _selectedGranularity; set => SetProperty(ref _selectedGranularity, value); }

    private ForecastReportDto? _report;
    public ForecastReportDto? Report { get => _report; set => SetProperty(ref _report, value); }

    private ForecastDashboardDto? _dashboard;
    public ForecastDashboardDto? Dashboard { get => _dashboard; set => SetProperty(ref _dashboard, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    private string _statusMessage = string.Empty;
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    public ObservableCollection<CashflowBreakdownRow> CashflowRows { get; } = new();
    public ObservableCollection<BudgetForecastRow> BudgetRows { get; } = new();
    public ObservableCollection<GoalForecastRow> GoalRows { get; } = new();

    public async Task LoadAsync()
    {
        if (_dashboardHandler != null)
        {
            try
            {
                Dashboard = await _dashboardHandler.HandleAsync(
                    DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
            }
            catch { /* Dashboard is advisory */ }
        }
    }

    private async Task RefreshBaselineAsync()
    {
        if (_baselineHandler == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Computing forecast...";

            var granularity = Enum.Parse<ForecastGranularity>(SelectedGranularity);
            Report = await _baselineHandler.HandleAsync(
                new GetBaselineForecastQuery(StartDate, EndDate, granularity),
                CancellationToken.None);

            CashflowRows.Clear();
            foreach (var r in Report.CashflowRows) CashflowRows.Add(r);

            BudgetRows.Clear();
            foreach (var r in Report.BudgetRows) BudgetRows.Add(r);

            GoalRows.Clear();
            foreach (var r in Report.GoalRows) GoalRows.Add(r);

            StatusMessage = $"Forecast computed: {Report.Summary.Warnings.Count} warnings";
            _toastService?.Success("Forecast refreshed");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
            _toastService?.Error($"Forecast error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportBaselineCsvAsync()
    {
        if (Report == null || _exportService == null) return;

        try
        {
            await _exportService.ExportCustomCsvAsync("forecast_baseline", writer =>
            {
                writer.WriteLine("# Forecast Baseline Report");
                writer.WriteLine($"# Horizon: {Report.Horizon.StartDate} to {Report.Horizon.EndDate}");
                writer.WriteLine($"# Currency: {Report.ReportingCurrency}");
                writer.WriteLine($"# Net Cashflow: {Report.Summary.KnownNetCashflow}");
                writer.WriteLine($"# End Balance: {Report.Summary.KnownEndBalance}");
                writer.WriteLine();

                writer.WriteLine("## Cashflow Breakdown");
                writer.WriteLine("Category,Amount,ReportingAmount");
                foreach (var r in Report.CashflowRows)
                    writer.WriteLine($"{r.Category},{r.Amount},{r.ReportingAmount}");

                writer.WriteLine();
                writer.WriteLine("## Budget Forecast");
                writer.WriteLine("Year,Month,Scope,Limit,Forecast,Remaining,Percent,Status");
                foreach (var r in Report.BudgetRows)
                    writer.WriteLine($"{r.Year},{r.Month},{r.ScopeLabel},{r.Limit},{r.ForecastActual},{r.Remaining},{r.Percent},{r.Status}");

                writer.WriteLine();
                writer.WriteLine("## Goal Forecast");
                writer.WriteLine("Name,Target,Contributed,Remaining,Progress,EstCompletion,Known,Currency");
                foreach (var r in Report.GoalRows)
                    writer.WriteLine($"{r.Name},{r.TargetAmount},{r.ForecastContributed},{r.Remaining},{r.ProgressPercent},{r.EstimatedCompletionDate},{r.IsKnown},{r.CurrencyCode}");
            });
            _toastService?.Success("Forecast exported");
        }
        catch (Exception ex) { _toastService?.Error($"Export error: {ex.Message}"); }
    }
}
