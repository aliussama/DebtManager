using DebtManager.Application.Models;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Dashboard view showing portfolio summary.
/// Uses GetPortfolioDashboardHandler for all data - no business logic in UI.
/// Wave B7: Also loads DashboardSummary for widget KPIs.
/// </summary>
public sealed class DashboardViewModel : ObservableObject
{
    private readonly GetPortfolioDashboardHandler _dashboardHandler;
    private readonly GetDashboardSummaryHandler? _summaryHandler;
    private readonly GetFinancialHealthHandler? _healthHandler;
    private readonly Action<Guid>? _onOpenObligation;
    private readonly Action? _onCreateObligation;
    private readonly GetSetupStateHandler? _setupStateHandler;
    private readonly Action? _onOpenSetupWizard;
    private readonly Action? _onNavigateToAccounts;
    private readonly Action? _onNavigateToImport;
    private readonly Func<Task>? _onSeedDemo;
    private readonly Action? _onNavigateToCashLedger;
    private readonly Action? _onNavigateToInvestments;
    private readonly Action? _onNavigateToGoals;
    private readonly Action? _onNavigateToObligations;
    private readonly Action? _onNavigateToForecast;
    private readonly Action? _onNavigateToAiAdvisor;
    private readonly Action? _onNavigateToReports;
    private readonly Action? _onNavigateToDataQuality;

    public DashboardViewModel(
        GetPortfolioDashboardHandler dashboardHandler,
        Action<Guid>? onOpenObligation = null,
        Action? onCreateObligation = null,
        GetSetupStateHandler? setupStateHandler = null,
        Action? onOpenSetupWizard = null,
        Action? onNavigateToAccounts = null,
        Action? onNavigateToImport = null,
        Func<Task>? onSeedDemo = null,
        Action? onNavigateToCashLedger = null,
        Action? onNavigateToInvestments = null,
        Action? onNavigateToGoals = null,
        Action? onNavigateToObligations = null,
        GetDashboardSummaryHandler? summaryHandler = null,
        Action? onNavigateToForecast = null,
        Action? onNavigateToAiAdvisor = null,
        Action? onNavigateToReports = null,
        Action? onNavigateToDataQuality = null,
        GetFinancialHealthHandler? healthHandler = null)
    {
        _dashboardHandler = dashboardHandler;
        _summaryHandler = summaryHandler;
        _healthHandler = healthHandler;
        _onOpenObligation = onOpenObligation;
        _onCreateObligation = onCreateObligation;
        _setupStateHandler = setupStateHandler;
        _onOpenSetupWizard = onOpenSetupWizard;
        _onNavigateToAccounts = onNavigateToAccounts;
        _onNavigateToImport = onNavigateToImport;
        _onSeedDemo = onSeedDemo;
        _onNavigateToCashLedger = onNavigateToCashLedger;
        _onNavigateToInvestments = onNavigateToInvestments;
        _onNavigateToGoals = onNavigateToGoals;
        _onNavigateToObligations = onNavigateToObligations;
        _onNavigateToForecast = onNavigateToForecast;
        _onNavigateToAiAdvisor = onNavigateToAiAdvisor;
        _onNavigateToReports = onNavigateToReports;
        _onNavigateToDataQuality = onNavigateToDataQuality;
        
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenObligationCommand = new RelayCommand<ObligationRowItem>(OpenObligation);
        CreateObligationCommand = new RelayCommand(() => _onCreateObligation?.Invoke());
        OpenSetupWizardCommand = new RelayCommand(() => _onOpenSetupWizard?.Invoke());
        NavigateToAccountsCommand = new RelayCommand(() => _onNavigateToAccounts?.Invoke());
        NavigateToImportCommand = new RelayCommand(() => _onNavigateToImport?.Invoke());
        SeedDemoCommand = new AsyncRelayCommand(SeedDemoAsync);
        NavigateToCashLedgerCommand = new RelayCommand(() => _onNavigateToCashLedger?.Invoke());
        NavigateToInvestmentsCommand = new RelayCommand(() => _onNavigateToInvestments?.Invoke());
        NavigateToGoalsCommand = new RelayCommand(() => _onNavigateToGoals?.Invoke());
        NavigateToObligationsCommand = new RelayCommand(() => _onNavigateToObligations?.Invoke());
        NavigateToForecastCommand = new RelayCommand(() => _onNavigateToForecast?.Invoke());
        NavigateToAiAdvisorCommand = new RelayCommand(() => _onNavigateToAiAdvisor?.Invoke());
        NavigateToReportsCommand = new RelayCommand(() => _onNavigateToReports?.Invoke());
        NavigateToDataQualityCommand = new RelayCommand(() => _onNavigateToDataQuality?.Invoke());
    }

    // Commands
    public ICommand RefreshCommand { get; }
    public ICommand OpenObligationCommand { get; }
    public ICommand CreateObligationCommand { get; }
    public ICommand OpenSetupWizardCommand { get; }
    public ICommand NavigateToAccountsCommand { get; }
    public ICommand NavigateToImportCommand { get; }
    public ICommand SeedDemoCommand { get; }
    public ICommand NavigateToCashLedgerCommand { get; }
    public ICommand NavigateToInvestmentsCommand { get; }
    public ICommand NavigateToGoalsCommand { get; }
    public ICommand NavigateToObligationsCommand { get; }
    public ICommand NavigateToForecastCommand { get; }
    public ICommand NavigateToAiAdvisorCommand { get; }
    public ICommand NavigateToReportsCommand { get; }
    public ICommand NavigateToDataQualityCommand { get; }

    // Selected obligation for navigation
    private ObligationRowItem? _selectedObligation;
    public ObligationRowItem? SelectedObligation
    {
        get => _selectedObligation;
        set => SetProperty(ref _selectedObligation, value);
    }

    private void OpenObligation(ObligationRowItem? item)
    {
        if (item != null)
        {
            _onOpenObligation?.Invoke(item.Id);
        }
    }

    // Properties
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private string _statusMessage = "Ready";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private DateOnly _asOfDate = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly AsOfDate
    {
        get => _asOfDate;
        set
        {
            if (SetProperty(ref _asOfDate, value))
            {
                // Refresh when date changes
                _ = RefreshAsync();
            }
        }
    }

    private string _currencyCode = "EGP";
    public string CurrencyCode
    {
        get => _currencyCode;
        set => SetProperty(ref _currencyCode, value);
    }

    // Summary statistics
    private decimal _totalPrincipal;
    public decimal TotalPrincipal
    {
        get => _totalPrincipal;
        set => SetProperty(ref _totalPrincipal, value);
    }

    private decimal _totalOutstanding;
    public decimal TotalOutstanding
    {
        get => _totalOutstanding;
        set => SetProperty(ref _totalOutstanding, value);
    }

    private decimal _totalPaid;
    public decimal TotalPaid
    {
        get => _totalPaid;
        set => SetProperty(ref _totalPaid, value);
    }

    private int _totalObligations;
    public int TotalObligations
    {
        get => _totalObligations;
        set => SetProperty(ref _totalObligations, value);
    }

    private int _activeObligations;
    public int ActiveObligations
    {
        get => _activeObligations;
        set => SetProperty(ref _activeObligations, value);
    }

    private int _closedObligations;
    public int ClosedObligations
    {
        get => _closedObligations;
        set => SetProperty(ref _closedObligations, value);
    }

    private int _healthyCount;
    public int HealthyCount
    {
        get => _healthyCount;
        set => SetProperty(ref _healthyCount, value);
    }

    private int _atRiskCount;
    public int AtRiskCount
    {
        get => _atRiskCount;
        set => SetProperty(ref _atRiskCount, value);
    }

    private int _overdueCount;
    public int OverdueCount
    {
        get => _overdueCount;
        set => SetProperty(ref _overdueCount, value);
    }

    private int _criticalCount;
    public int CriticalCount
    {
        get => _criticalCount;
        set => SetProperty(ref _criticalCount, value);
    }

    private decimal _upcomingDue7Days;
    public decimal UpcomingDue7Days
    {
        get => _upcomingDue7Days;
        set => SetProperty(ref _upcomingDue7Days, value);
    }

    private decimal _upcomingDue30Days;
    public decimal UpcomingDue30Days
    {
        get => _upcomingDue30Days;
        set => SetProperty(ref _upcomingDue30Days, value);
    }

    private int _upcomingPaymentsCount7Days;
    public int UpcomingPaymentsCount7Days
    {
        get => _upcomingPaymentsCount7Days;
        set => SetProperty(ref _upcomingPaymentsCount7Days, value);
    }

    private int _overdueInstallmentsCount;
    public int OverdueInstallmentsCount
    {
        get => _overdueInstallmentsCount;
        set => SetProperty(ref _overdueInstallmentsCount, value);
    }

    private decimal _totalOverdueAmount;
    public decimal TotalOverdueAmount
    {
        get => _totalOverdueAmount;
        set => SetProperty(ref _totalOverdueAmount, value);
    }

    // B7 Widget KPI properties
    private decimal _widgetCashBalance;
    public decimal WidgetCashBalance
    {
        get => _widgetCashBalance;
        set => SetProperty(ref _widgetCashBalance, value);
    }

    private decimal _widgetNetWorth;
    public decimal WidgetNetWorth
    {
        get => _widgetNetWorth;
        set => SetProperty(ref _widgetNetWorth, value);
    }

    private decimal _widgetBudgetHealthPercent;
    public decimal WidgetBudgetHealthPercent
    {
        get => _widgetBudgetHealthPercent;
        set => SetProperty(ref _widgetBudgetHealthPercent, value);
    }

    private int _widgetOverdueObligationCount;
    public int WidgetOverdueObligationCount
    {
        get => _widgetOverdueObligationCount;
        set => SetProperty(ref _widgetOverdueObligationCount, value);
    }

    private int _widgetAiInsightCount;
    public int WidgetAiInsightCount
    {
        get => _widgetAiInsightCount;
        set => SetProperty(ref _widgetAiInsightCount, value);
    }

    private int _widgetDataQualityIssueCount;
    public int WidgetDataQualityIssueCount
    {
        get => _widgetDataQualityIssueCount;
        set => SetProperty(ref _widgetDataQualityIssueCount, value);
    }

    public ObservableCollection<WidgetUpcomingPaymentItem> WidgetUpcomingPayments { get; } = new();
    public ObservableCollection<WidgetGoalProgressItem> WidgetTopGoals { get; } = new();

    // B8 Financial Health Score properties
    private int _healthScore;
    public int HealthScore
    {
        get => _healthScore;
        set => SetProperty(ref _healthScore, value);
    }

    private string _healthGrade = "?";
    public string HealthGrade
    {
        get => _healthGrade;
        set => SetProperty(ref _healthGrade, value);
    }

    public ObservableCollection<HealthComponentItem> HealthComponents { get; } = new();

    // Empty state helpers
    public bool HasObligations => Obligations.Count > 0;
    public bool HasNoObligations => Obligations.Count == 0;

    private bool _showGettingStarted;
    public bool ShowGettingStarted
    {
        get => _showGettingStarted;
        set => SetProperty(ref _showGettingStarted, value);
    }

    // Collections
    public ObservableCollection<ObligationRowItem> Obligations { get; } = new();
    public ObservableCollection<UpcomingPaymentItem> UpcomingPayments { get; } = new();

    public async Task RefreshAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading dashboard...";

        try
        {
            var query = new GetPortfolioDashboardQuery(AsOfDate, CurrencyCode);
            var dashboard = await _dashboardHandler.HandleAsync(query, CancellationToken.None);

            // Map dashboard to ViewModel properties
            TotalPrincipal = dashboard.TotalPrincipal.Amount;
            TotalPaid = dashboard.TotalPaid.Amount;
            TotalOutstanding = dashboard.TotalOutstanding.Amount;
            TotalObligations = dashboard.TotalObligations;
            ActiveObligations = dashboard.ActiveObligations;
            ClosedObligations = dashboard.ClosedObligations;
            HealthyCount = dashboard.HealthyObligations;
            AtRiskCount = dashboard.AtRiskObligations;
            OverdueCount = dashboard.DelinquentObligations;
            CriticalCount = dashboard.CriticalObligations;
            UpcomingDue7Days = dashboard.TotalDueNext7Days.Amount;
            UpcomingDue30Days = dashboard.TotalDueNext30Days.Amount;
            UpcomingPaymentsCount7Days = dashboard.UpcomingPaymentsNext7Days;
            OverdueInstallmentsCount = dashboard.OverdueInstallmentsCount;
            TotalOverdueAmount = dashboard.TotalOverdueAmount.Amount;
            CurrencyCode = dashboard.CurrencyCode;

            // Map obligations to UI items
            Obligations.Clear();
            foreach (var summary in dashboard.Obligations)
            {
                Obligations.Add(new ObligationRowItem(
                    Id: summary.ObligationId,
                    Name: summary.Name,
                    Type: summary.ObligationType,
                    Principal: summary.Principal.Amount,
                    Paid: summary.TotalPaid.Amount,
                    Outstanding: summary.OutstandingBalance.Amount,
                    OverdueInstallments: summary.OverdueInstallments,
                    NextDueDate: summary.NextDueDate,
                    NextPaymentAmount: summary.NextPaymentAmount?.Amount,
                    DaysUntilNextDue: summary.DaysUntilNextDue,
                    HealthStatus: summary.HealthStatus.ToString(),
                    IsClosed: summary.IsClosed,
                    CurrencyCode: summary.Principal.Currency.Code
                ));
            }

            // Notify empty state properties
            OnPropertyChanged(nameof(HasObligations));
            OnPropertyChanged(nameof(HasNoObligations));

            // Load B7 widget summary
            await LoadWidgetSummaryAsync();

            // Load B8 health score
            await LoadHealthScoreAsync();

            // Determine Getting Started visibility
            await CheckGettingStartedAsync();

            StatusMessage = $"Dashboard loaded. {TotalObligations} obligations found.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task CheckGettingStartedAsync()
    {
        if (_setupStateHandler == null)
        {
            ShowGettingStarted = Obligations.Count == 0;
            return;
        }

        try
        {
            var state = await _setupStateHandler.HandleAsync(CancellationToken.None);
            ShowGettingStarted = !state.IsInitialSetupCompleted || Obligations.Count == 0;
        }
        catch
        {
            ShowGettingStarted = Obligations.Count == 0;
        }
    }

    private async Task LoadWidgetSummaryAsync()
    {
        if (_summaryHandler == null)
            return;

        try
        {
            var summary = await _summaryHandler.HandleAsync(AsOfDate, CancellationToken.None);

            WidgetCashBalance = summary.TotalCashBalance;
            WidgetNetWorth = summary.NetWorth;
            WidgetBudgetHealthPercent = summary.BudgetHealthPercent;
            WidgetOverdueObligationCount = summary.OverdueObligationCount;
            WidgetAiInsightCount = summary.AiInsightCount;
            WidgetDataQualityIssueCount = summary.DataQualityIssueCount;

            WidgetUpcomingPayments.Clear();
            foreach (var p in summary.UpcomingPayments)
            {
                WidgetUpcomingPayments.Add(new WidgetUpcomingPaymentItem(
                    p.EntityId, p.Title, p.DueDate, p.Amount, p.CurrencyCode));
            }

            WidgetTopGoals.Clear();
            foreach (var g in summary.TopGoals)
            {
                WidgetTopGoals.Add(new WidgetGoalProgressItem(
                    g.GoalId, g.Name, g.ProgressPercent, g.TargetAmount,
                    g.ContributedAmount, g.TargetDate));
            }
        }
        catch
        {
            // Widget data is supplementary — tolerate failures gracefully
        }
    }

    private async Task LoadHealthScoreAsync()
    {
        if (_healthHandler == null)
            return;

        try
        {
            var healthScore = await _healthHandler.HandleAsync(AsOfDate, evaluationMonths: 3, CancellationToken.None);

            HealthScore = healthScore.Score;
            HealthGrade = healthScore.Grade;

            HealthComponents.Clear();
            foreach (var c in healthScore.Components)
            {
                HealthComponents.Add(new HealthComponentItem(
                    c.Name, c.Value, c.Weight, c.Status));
            }
        }
        catch
        {
            // Health score is supplementary - tolerate failures gracefully
            HealthScore = 0;
            HealthGrade = "?";
        }
    }

    private async Task SeedDemoAsync()
    {
        if (_onSeedDemo != null)
            await _onSeedDemo();
    }
}

/// <summary>
/// Row item for an obligation in the dashboard grid.
/// </summary>
public sealed record ObligationRowItem(
    Guid Id,
    string Name,
    string Type,
    decimal Principal,
    decimal Paid,
    decimal Outstanding,
    int OverdueInstallments,
    DateOnly? NextDueDate,
    decimal? NextPaymentAmount,
    int DaysUntilNextDue,
    string HealthStatus,
    bool IsClosed,
    string CurrencyCode
)
{
    public string Status => IsClosed ? "Closed" : (Outstanding <= 0 ? "Paid Off" : "Active");
    public string NextDueDateDisplay => NextDueDate?.ToString("MMM dd, yyyy") ?? "—";
    public string NextPaymentDisplay => NextPaymentAmount.HasValue ? $"{NextPaymentAmount:N2}" : "—";
}

/// <summary>
/// Upcoming payment item for the upcoming payments list.
/// </summary>
public sealed record UpcomingPaymentItem(
    DateOnly DueDate,
    string ObligationName,
    decimal Amount,
    string CurrencyCode,
    int DaysUntilDue,
    bool IsOverdue
);

/// <summary>
/// B7 Widget: upcoming payment row for next 7 days.
/// </summary>
public sealed record WidgetUpcomingPaymentItem(
    Guid EntityId,
    string Title,
    DateOnly DueDate,
    decimal Amount,
    string CurrencyCode
)
{
    public string DueDateDisplay => DueDate.ToString("MMM dd");
    public string AmountDisplay => $"{Amount:N0}";
}

/// <summary>
/// B7 Widget: goal progress row.
/// </summary>
public sealed record WidgetGoalProgressItem(
    Guid GoalId,
    string Name,
    decimal ProgressPercent,
    decimal TargetAmount,
    decimal ContributedAmount,
    DateOnly? TargetDate
)
{
    public string ProgressDisplay => $"{ProgressPercent:N0}%";
    public double ProgressRatio => (double)Math.Min(ProgressPercent, 100m) / 100.0;
    public string TargetDateDisplay => TargetDate?.ToString("MMM yyyy") ?? "?";
}

/// <summary>
/// B8 Widget: Health component breakdown item.
/// </summary>
public sealed record HealthComponentItem(
    string Name,
    decimal Value,
    decimal Weight,
    string Status
)
{
    public string ValueDisplay => $"{Value:N2}";
    public string WeightDisplay => $"{Weight * 100:N0}%";
}

