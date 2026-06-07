using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Planning;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class RetirementViewModel : ObservableObject
{
    private readonly DefineRetirementProfileHandler _profileHandler;
    private readonly SetRetirementAssumptionsHandler _assumptionsHandler;
    private readonly ArchiveRetirementAssumptionsHandler _archiveAssumptionsHandler;
    private readonly GetRetirementPlanReportHandler _reportHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;

    public RetirementViewModel(
        DefineRetirementProfileHandler profileHandler,
        SetRetirementAssumptionsHandler assumptionsHandler,
        ArchiveRetirementAssumptionsHandler archiveAssumptionsHandler,
        GetRetirementPlanReportHandler reportHandler,
        Guid actorUserId, Guid deviceId,
        IToastService? toastService = null,
        IExportService? exportService = null)
    {
        _profileHandler = profileHandler;
        _assumptionsHandler = assumptionsHandler;
        _archiveAssumptionsHandler = archiveAssumptionsHandler;
        _reportHandler = reportHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;

        SaveProfileCommand = new AsyncRelayCommand(SaveProfileAsync);
        SaveAssumptionsCommand = new AsyncRelayCommand(SaveAssumptionsAsync);
        RunPlanCommand = new AsyncRelayCommand(RunPlanAsync);
        ExportCsvCommand = new AsyncRelayCommand(ExportAsync);

        // Defaults
        ProfileName = "My Retirement Plan";
        RetirementDate = DateTime.Today.AddYears(25);
        DesiredMonthlySpending = 5000m;
        SpendingCurrency = "EGP";
        LifeExpectancyYears = 85;
        SafeWithdrawalRate = 0.04m;
        ExpectedReturnRate = 0.07m;
        ExpectedInflation = 0.03m;
        ExpectedSalaryGrowth = 0.02m;
        MonthlySavings = 1000m;
        SavingsCurrency = "EGP";
        ReportingCurrency = "EGP";
    }

    public ICommand SaveProfileCommand { get; }
    public ICommand SaveAssumptionsCommand { get; }
    public ICommand RunPlanCommand { get; }
    public ICommand ExportCsvCommand { get; }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _hasReport;
    public bool HasReport { get => _hasReport; set => SetProperty(ref _hasReport, value); }

    private string _errorMessage = string.Empty;
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    // Profile fields
    private string _profileName = string.Empty;
    public string ProfileName { get => _profileName; set => SetProperty(ref _profileName, value); }

    private DateTime _retirementDate;
    public DateTime RetirementDate { get => _retirementDate; set => SetProperty(ref _retirementDate, value); }

    private decimal _desiredMonthlySpending;
    public decimal DesiredMonthlySpending { get => _desiredMonthlySpending; set => SetProperty(ref _desiredMonthlySpending, value); }

    private string _spendingCurrency = "EGP";
    public string SpendingCurrency { get => _spendingCurrency; set => SetProperty(ref _spendingCurrency, value); }

    private int _lifeExpectancyYears;
    public int LifeExpectancyYears { get => _lifeExpectancyYears; set => SetProperty(ref _lifeExpectancyYears, value); }

    private decimal _safeWithdrawalRate;
    public decimal SafeWithdrawalRate { get => _safeWithdrawalRate; set => SetProperty(ref _safeWithdrawalRate, value); }

    // Assumptions fields
    private decimal _expectedReturnRate;
    public decimal ExpectedReturnRate { get => _expectedReturnRate; set => SetProperty(ref _expectedReturnRate, value); }

    private decimal _expectedInflation;
    public decimal ExpectedInflation { get => _expectedInflation; set => SetProperty(ref _expectedInflation, value); }

    private decimal _expectedSalaryGrowth;
    public decimal ExpectedSalaryGrowth { get => _expectedSalaryGrowth; set => SetProperty(ref _expectedSalaryGrowth, value); }

    private decimal _monthlySavings;
    public decimal MonthlySavings { get => _monthlySavings; set => SetProperty(ref _monthlySavings, value); }

    private string _savingsCurrency = "EGP";
    public string SavingsCurrency { get => _savingsCurrency; set => SetProperty(ref _savingsCurrency, value); }

    private string _reportingCurrency = "EGP";
    public string ReportingCurrency { get => _reportingCurrency; set => SetProperty(ref _reportingCurrency, value); }

    // Report result
    private RetirementPlanResult? _plan;
    public RetirementPlanResult? Plan { get => _plan; set => SetProperty(ref _plan, value); }

    public ObservableCollection<string> Warnings { get; } = new();

    public async Task LoadAsync()
    {
        await RunPlanAsync();
    }

    private async Task SaveProfileAsync()
    {
        try
        {
            await _profileHandler.HandleAsync(
                new DefineRetirementProfileCommand(null, ProfileName,
                    DateOnly.FromDateTime(RetirementDate),
                    DesiredMonthlySpending, SpendingCurrency,
                    LifeExpectancyYears, "SafeWithdrawalRate",
                    SafeWithdrawalRate, DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Retirement profile saved");
            await RunPlanAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to save profile", ex); }
    }

    private async Task SaveAssumptionsAsync()
    {
        try
        {
            await _assumptionsHandler.HandleAsync(
                new SetRetirementAssumptionsCommand(null, "Assumptions",
                    ExpectedReturnRate, ExpectedInflation, ExpectedSalaryGrowth,
                    MonthlySavings, SavingsCurrency,
                    ReportingCurrency, DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Retirement assumptions saved");
            await RunPlanAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to save assumptions", ex); }
    }

    private async Task RunPlanAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        HasReport = false;
        Plan = null;
        Warnings.Clear();

        try
        {
            var result = await _reportHandler.HandleAsync(
                DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                ErrorMessage = result.ErrorMessage;
                HasReport = false;
                return;
            }

            Plan = result.Plan;
            HasReport = Plan != null;

            if (Plan != null)
            {
                foreach (var w in Plan.Warnings)
                    Warnings.Add(w);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to generate retirement plan: {ex.Message}";
            _toastService?.Error("Failed to generate plan", ex);
        }
        finally { IsLoading = false; }
    }

    private async Task ExportAsync()
    {
        if (_exportService == null || Plan == null) return;
        try
        {
            var headers = new[] {
                "AsOfDate", "RetirementDate", "ReportingCurrency", "YearsToRetirement",
                "CurrentNetWorthKnown", "UnknownValueCount", "MonthlySpendingAtRetirement",
                "RequiredCorpus", "ProjectedCorpus", "FundingGap", "RequiredMonthlySavings",
                "SafeWithdrawalRate", "ReturnRate", "InflationRate" };
            var p = Plan;
            var rows = new List<IReadOnlyList<string?>>
            {
                new[] {
                    p.AsOfDate.ToString("yyyy-MM-dd"), p.RetirementDate.ToString("yyyy-MM-dd"),
                    p.ReportingCurrencyCode, p.YearsToRetirement.ToString("F2"),
                    p.CurrentNetWorthKnown.ToString("F2"), p.UnknownValueCount.ToString(),
                    p.MonthlySpendingAtRetirementInflationAdjusted.ToString("F2"),
                    p.RequiredCorpusAtRetirement.ToString("F2"), p.ProjectedCorpusAtRetirement.ToString("F2"),
                    p.FundingGap.ToString("F2"), p.RequiredMonthlySavings.ToString("F2"),
                    p.SafeWithdrawalRate.ToString("F4"), p.ReturnRate.ToString("F4"),
                    p.InflationRate.ToString("F4")
                }
            };
            await _exportService.ExportCsvAsync("RetirementPlan", headers, rows);
        }
        catch (Exception ex) { _toastService?.Error("Export failed", ex); }
    }
}
