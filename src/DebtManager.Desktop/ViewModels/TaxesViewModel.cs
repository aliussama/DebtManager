using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class TaxesViewModel : ObservableObject
{
    private readonly CreateTaxProfileHandler _createProfileHandler;
    private readonly ModifyTaxProfileHandler _modifyProfileHandler;
    private readonly ArchiveTaxProfileHandler _archiveProfileHandler;
    private readonly DefineTaxRuleHandler _defineRuleHandler;
    private readonly ArchiveTaxRuleHandler _archiveRuleHandler;
    private readonly ConfirmTaxClassificationHandler _confirmHandler;
    private readonly GetTaxProfilesHandler _getProfilesHandler;
    private readonly GetTaxRulesHandler _getRulesHandler;
    private readonly GetTaxYearReportHandler _reportHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;

    public TaxesViewModel(
        CreateTaxProfileHandler createProfileHandler,
        ModifyTaxProfileHandler modifyProfileHandler,
        ArchiveTaxProfileHandler archiveProfileHandler,
        DefineTaxRuleHandler defineRuleHandler,
        ArchiveTaxRuleHandler archiveRuleHandler,
        ConfirmTaxClassificationHandler confirmHandler,
        GetTaxProfilesHandler getProfilesHandler,
        GetTaxRulesHandler getRulesHandler,
        GetTaxYearReportHandler reportHandler,
        Guid actorUserId,
        Guid deviceId,
        IToastService? toastService = null,
        IExportService? exportService = null)
    {
        _createProfileHandler = createProfileHandler;
        _modifyProfileHandler = modifyProfileHandler;
        _archiveProfileHandler = archiveProfileHandler;
        _defineRuleHandler = defineRuleHandler;
        _archiveRuleHandler = archiveRuleHandler;
        _confirmHandler = confirmHandler;
        _getProfilesHandler = getProfilesHandler;
        _getRulesHandler = getRulesHandler;
        _reportHandler = reportHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        CreateProfileCommand = new AsyncRelayCommand(CreateProfileAsync);
        ArchiveProfileCommand = new AsyncRelayCommand(ArchiveProfileAsync);
        DefineRuleCommand = new AsyncRelayCommand(DefineRuleAsync);
        ArchiveRuleCommand = new AsyncRelayCommand(ArchiveRuleAsync);
        GenerateReportCommand = new AsyncRelayCommand(GenerateReportAsync);
        ExportReportCommand = new AsyncRelayCommand(ExportReportAsync);

        SelectedTab = 0;
        ReportTaxYear = DateTime.Today.Year;
    }

    public ICommand RefreshCommand { get; }
    public ICommand CreateProfileCommand { get; }
    public ICommand ArchiveProfileCommand { get; }
    public ICommand DefineRuleCommand { get; }
    public ICommand ArchiveRuleCommand { get; }
    public ICommand GenerateReportCommand { get; }
    public ICommand ExportReportCommand { get; }

    public ObservableCollection<TaxProfileDto> Profiles { get; } = new();
    public ObservableCollection<TaxRuleDto> Rules { get; } = new();
    public ObservableCollection<CapitalGainLineDto> CapitalGains { get; } = new();
    public ObservableCollection<IncomeLineDto> IncomeLines { get; } = new();
    public ObservableCollection<DeductionLineDto> Deductions { get; } = new();
    public ObservableCollection<UnclassifiedLineDto> UnclassifiedItems { get; } = new();

    private int _selectedTab;
    public int SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    // --- Profile form ---
    private string _newProfileName = string.Empty;
    public string NewProfileName
    {
        get => _newProfileName;
        set => SetProperty(ref _newProfileName, value);
    }

    private string _newProfileCountryCode = "US";
    public string NewProfileCountryCode
    {
        get => _newProfileCountryCode;
        set => SetProperty(ref _newProfileCountryCode, value);
    }

    private int _newProfileStartMonth = 1;
    public int NewProfileStartMonth
    {
        get => _newProfileStartMonth;
        set => SetProperty(ref _newProfileStartMonth, value);
    }

    private int _newProfileStartDay = 1;
    public int NewProfileStartDay
    {
        get => _newProfileStartDay;
        set => SetProperty(ref _newProfileStartDay, value);
    }

    private string _newProfileBaseCurrency = "USD";
    public string NewProfileBaseCurrency
    {
        get => _newProfileBaseCurrency;
        set => SetProperty(ref _newProfileBaseCurrency, value);
    }

    private TaxProfileDto? _selectedProfile;
    public TaxProfileDto? SelectedProfile
    {
        get => _selectedProfile;
        set => SetProperty(ref _selectedProfile, value);
    }

    // --- Rule form ---
    private string _newRuleAppliesTo = "ExpenseCategory";
    public string NewRuleAppliesTo
    {
        get => _newRuleAppliesTo;
        set => SetProperty(ref _newRuleAppliesTo, value);
    }

    private string _newRuleMatchValue = string.Empty;
    public string NewRuleMatchValue
    {
        get => _newRuleMatchValue;
        set => SetProperty(ref _newRuleMatchValue, value);
    }

    private string _newRuleTaxCategory = "DeductibleExpense";
    public string NewRuleTaxCategory
    {
        get => _newRuleTaxCategory;
        set => SetProperty(ref _newRuleTaxCategory, value);
    }

    private TaxRuleDto? _selectedRule;
    public TaxRuleDto? SelectedRule
    {
        get => _selectedRule;
        set => SetProperty(ref _selectedRule, value);
    }

    // --- Report ---
    private int _reportTaxYear;
    public int ReportTaxYear
    {
        get => _reportTaxYear;
        set => SetProperty(ref _reportTaxYear, value);
    }

    private TaxYearReportDto? _currentReport;
    public TaxYearReportDto? CurrentReport
    {
        get => _currentReport;
        set => SetProperty(ref _currentReport, value);
    }

    private string _reportSummary = string.Empty;
    public string ReportSummary
    {
        get => _reportSummary;
        set => SetProperty(ref _reportSummary, value);
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var profiles = await _getProfilesHandler.HandleAsync(CancellationToken.None);
            Profiles.Clear();
            foreach (var p in profiles)
                Profiles.Add(p);

            var rules = await _getRulesHandler.HandleAsync(CancellationToken.None);
            Rules.Clear();
            foreach (var r in rules)
                Rules.Add(r);
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to load tax data", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task CreateProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName))
        {
            _toastService?.Error("Profile name is required");
            return;
        }

        try
        {
            await _createProfileHandler.HandleAsync(
                new CreateTaxProfileCommand(null, NewProfileName.Trim(), NewProfileCountryCode,
                    NewProfileStartMonth, NewProfileStartDay, NewProfileBaseCurrency,
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);

            _toastService?.Success("Tax profile created");
            NewProfileName = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to create profile", ex);
        }
    }

    private async Task ArchiveProfileAsync()
    {
        if (SelectedProfile == null)
        {
            _toastService?.Error("Select a profile first");
            return;
        }

        try
        {
            await _archiveProfileHandler.HandleAsync(
                new ArchiveTaxProfileCommand(SelectedProfile.ProfileId,
                    DateOnly.FromDateTime(DateTime.Today), "Archived by user"),
                _actorUserId, _deviceId, CancellationToken.None);

            _toastService?.Success("Tax profile archived");
            SelectedProfile = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to archive profile", ex);
        }
    }

    private async Task DefineRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(NewRuleMatchValue))
        {
            _toastService?.Error("Match value is required");
            return;
        }

        try
        {
            await _defineRuleHandler.HandleAsync(
                new DefineTaxRuleCommand(null, NewRuleAppliesTo, NewRuleMatchValue.Trim(),
                    NewRuleTaxCategory, DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);

            _toastService?.Success("Tax rule defined");
            NewRuleMatchValue = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to define rule", ex);
        }
    }

    private async Task ArchiveRuleAsync()
    {
        if (SelectedRule == null)
        {
            _toastService?.Error("Select a rule first");
            return;
        }

        try
        {
            await _archiveRuleHandler.HandleAsync(
                new ArchiveTaxRuleCommand(SelectedRule.RuleId,
                    DateOnly.FromDateTime(DateTime.Today), "Archived by user"),
                _actorUserId, _deviceId, CancellationToken.None);

            _toastService?.Success("Tax rule archived");
            SelectedRule = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to archive rule", ex);
        }
    }

    private async Task GenerateReportAsync()
    {
        if (SelectedProfile == null)
        {
            _toastService?.Error("Select a tax profile first");
            return;
        }

        IsLoading = true;
        try
        {
            var report = await _reportHandler.HandleAsync(
                SelectedProfile.ProfileId, ReportTaxYear, CancellationToken.None);

            CurrentReport = report;

            CapitalGains.Clear();
            foreach (var cg in report.CapitalGains) CapitalGains.Add(cg);

            IncomeLines.Clear();
            foreach (var inc in report.IncomeLines) IncomeLines.Add(inc);

            Deductions.Clear();
            foreach (var ded in report.Deductions) Deductions.Add(ded);

            UnclassifiedItems.Clear();
            foreach (var unc in report.UnclassifiedItems) UnclassifiedItems.Add(unc);

            ReportSummary = $"Capital Gains: {report.TotalCapitalGains:N2} | " +
                            $"Dividends: {report.TotalDividendIncome:N2} | " +
                            $"Interest: {report.TotalInterestIncome:N2} | " +
                            $"Other Income: {report.TotalOtherIncome:N2} | " +
                            $"Deductions: {report.TotalDeductions:N2} | " +
                            $"Unclassified: {report.UnclassifiedCount} | " +
                            $"Unknown: {report.UnknownValueCount}";

            _toastService?.Success("Tax year report generated");
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to generate report", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ExportReportAsync()
    {
        if (_exportService == null || CurrentReport == null)
        {
            _toastService?.Error("Generate a report first");
            return;
        }

        try
        {
            var report = CurrentReport;
            await _exportService.ExportCustomCsvAsync(
                $"TaxReport_{report.ProfileName}_{report.TaxYear}.csv",
                writer => GetTaxYearReportHandler.WriteCsvReport(report, writer));
        }
        catch (Exception ex)
        {
            _toastService?.Error("Export failed", ex);
        }
    }
}
