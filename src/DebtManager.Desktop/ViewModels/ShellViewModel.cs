using DebtManager.Application.Identity;
using DebtManager.Application.Simulation;
using DebtManager.Application.UseCases;
using DebtManager.Desktop.Recovery;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Events;
using DebtManager.Domain.Rules;
using DebtManager.Infrastructure.Diagnostics;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Security;
using DebtManager.Sync;
using System.Net.Http;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

/// <summary>
/// Shell ViewModel that manages navigation and hosts child ViewModels.
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    private readonly IEventStore _eventStore;
    private readonly IRuleEngine _ruleEngine;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly ISecurityAuditLogger? _auditLogger;
    private readonly CreateObligationHandler? _createObligationHandler;
    private readonly RecordPaymentHandler? _recordPaymentHandler;
    private readonly DefineScheduleHandler? _defineScheduleHandler;
    private readonly GetFinancialSnapshotHandler? _snapshotHandler;
    private readonly CloseObligationHandler? _closeObligationHandler;
    private readonly PreviewPaymentAllocationHandler? _previewPaymentHandler;
    private readonly GetPaymentsLedgerHandler? _ledgerHandler;
    private readonly ReversePaymentHandler? _reversePaymentHandler;
    private readonly SyncEngine? _syncEngine;
    private readonly SecureConfiguration? _secureConfiguration;
    private readonly IToastService? _toastService;
    private readonly IFocusRequestService? _focusService;
    private readonly IThemeService? _themeService;
    private readonly IAppReloadService? _reloadService;
    private readonly string? _vaultId;
    private CancellationTokenSource? _syncCts;
    private readonly Dictionary<string, DateTimeOffset> _viewWatermarks = new();
    private DateTimeOffset _globalEventWatermark = DateTimeOffset.MinValue;

    // Views that always refresh lightweight summaries on navigation
    private static readonly HashSet<string> _alwaysRefreshViews = new()
    {
        "Dashboard", "CashLedger", "PortfolioInvestments", "NetWorth", "Budgets"
    };

    public ShellViewModel(
        IEventStore eventStore,
        IRuleEngine ruleEngine,
        DashboardViewModel dashboardViewModel,
        Guid actorUserId,
        Guid deviceId,
        ISecurityAuditLogger? auditLogger = null,
        CreateObligationHandler? createObligationHandler = null,
        RecordPaymentHandler? recordPaymentHandler = null,
        DefineScheduleHandler? defineScheduleHandler = null,
        GetFinancialSnapshotHandler? snapshotHandler = null,
        CloseObligationHandler? closeObligationHandler = null,
        PreviewPaymentAllocationHandler? previewPaymentHandler = null,
        GetPaymentsLedgerHandler? ledgerHandler = null,
        ReversePaymentHandler? reversePaymentHandler = null,
        SyncEngine? syncEngine = null,
        string? vaultId = null,
        SecureConfiguration? secureConfiguration = null,
        IToastService? toastService = null,
        ToastHostViewModel? toastHost = null,
        RulePackManagerViewModel? rulePackManagerVm = null,
        ObligationsListViewModel? obligationsListVm = null,
        PaymentsListViewModel? paymentsListVm = null,
        ScenarioSimulationViewModel? simulationVm = null,
        AuditTrailViewModel? auditVm = null,
        ChargeBreakdownViewModel? chargeBreakdownVm = null,
        IFocusRequestService? focusService = null,
        IThemeService? themeService = null,
        IVaultBackupService? backupService = null,
        IAppReloadService? reloadService = null,
        AccountsViewModel? accountsVm = null,
        CashLedgerViewModel? cashLedgerVm = null,
        CategoriesViewModel? categoriesVm = null,
        BudgetsViewModel? budgetsVm = null,
        RecurringViewModel? recurringVm = null,
        ImportViewModel? importVm = null,
        AssetsViewModel? assetsVm = null,
        NetWorthViewModel? netWorthVm = null,
        InvestmentAccountsViewModel? investmentAccountsVm = null,
        PortfolioViewModel? portfolioInvestmentsVm = null,
        HoldingDetailViewModel? holdingDetailVm = null,
        TaxesViewModel? taxesVm = null,
        GoalsViewModel? goalsVm = null,
        RetirementViewModel? retirementVm = null,
        DataQualityViewModel? dataQualityVm = null,
        CurrencySettingsViewModel? currencySettingsVm = null,
        ForecastViewModel? forecastVm = null,
        ScenariosViewModel? scenariosVm = null,
        PartiesViewModel? partiesVm = null,
        ContractsViewModel? contractsVm = null,
        BillsViewModel? billsVm = null,
        InvoicesViewModel? invoicesVm = null,
        NotificationsViewModel? notificationsVm = null,
        DocumentVaultViewModel? documentVaultVm = null,
        ImportRulesViewModel? importRulesVm = null,
        AiAdvisorViewModel? aiAdvisorVm = null,
        IncomeSourcesViewModel? incomeSourcesVm = null,
        ReportsViewModel? reportsVm = null,
        GetSetupStateHandler? getSetupStateHandler = null,
        CompleteInitialSetupHandler? completeSetupHandler = null,
        CreateDefaultAccountsHandler? createDefaultAccountsHandler = null,
        CreateDefaultCategoriesHandler? createDefaultCategoriesHandler = null,
        SeedDemoDataHandler? seedDemoHandler = null)
    {
        _eventStore = eventStore;
        _ruleEngine = ruleEngine;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _auditLogger = auditLogger;
        _createObligationHandler = createObligationHandler;
        _recordPaymentHandler = recordPaymentHandler;
        _defineScheduleHandler = defineScheduleHandler;
        _snapshotHandler = snapshotHandler;
        _closeObligationHandler = closeObligationHandler;
        _previewPaymentHandler = previewPaymentHandler;
        _ledgerHandler = ledgerHandler;
        _reversePaymentHandler = reversePaymentHandler;
        _syncEngine = syncEngine;
        _vaultId = vaultId;
        _secureConfiguration = secureConfiguration;
        _toastService = toastService;
        _focusService = focusService;
        _themeService = themeService;
        _reloadService = reloadService;
        ToastHost = toastHost;
        RulePackManagerVm = rulePackManagerVm;

        // Initialize child ViewModels
        Dashboard = dashboardViewModel;
        PortfolioStatus = new PortfolioStatusViewModel(
            (SqliteEventStore)eventStore,
            ruleEngine);

        // Initialize list ViewModels
        ObligationsListVm = obligationsListVm ?? new ObligationsListViewModel(onCreateObligation: ShowCreateObligation);
        PaymentsListVm = paymentsListVm ?? new PaymentsListViewModel(onRecordPayment: ShowRecordPayment);
        ReportsVm = reportsVm;
        SimulationVm = simulationVm;
        AuditVm = auditVm;
        ChargeBreakdownVm = chargeBreakdownVm;
        AccountsVm = accountsVm;
        CashLedgerVm = cashLedgerVm;
        CategoriesVm = categoriesVm;
        BudgetsVm = budgetsVm;
        RecurringVm = recurringVm;
        ImportVm = importVm;
        AssetsVm = assetsVm;
        NetWorthVm = netWorthVm;
        InvestmentAccountsVm = investmentAccountsVm;
        PortfolioInvestmentsVm = portfolioInvestmentsVm;
        HoldingDetailVm = holdingDetailVm;
        TaxesVm = taxesVm;
        GoalsVm = goalsVm;
        RetirementVm = retirementVm;
        DataQualityVm = dataQualityVm;
        CurrencySettingsVm = currencySettingsVm;
        ForecastVm = forecastVm;
        ScenariosVm = scenariosVm;
        PartiesVm = partiesVm;
        ContractsVm = contractsVm;
        BillsVm = billsVm;
        InvoicesVm = invoicesVm;
        NotificationsVm = notificationsVm;
        DocumentVaultVm = documentVaultVm;
        ImportRulesVm = importRulesVm;
        AiAdvisorVm = aiAdvisorVm;
        IncomeSourcesVm = incomeSourcesVm;
        _getSetupStateHandler = getSetupStateHandler;
        _completeSetupHandler = completeSetupHandler;
        _createDefaultAccountsHandler = createDefaultAccountsHandler;
        _createDefaultCategoriesHandler = createDefaultCategoriesHandler;
        _seedDemoHandler = seedDemoHandler;

        // Initialize Settings ViewModel
        if (_secureConfiguration != null)
        {
            Settings = new SettingsViewModel(_secureConfiguration, OnSyncConfigChanged, _toastService, onRunOnboarding: StartOnboarding, themeService: _themeService, backupService: backupService);
        }

        // Commands
        NavigateToDashboardCommand = new RelayCommand(() => NavigateToDashboard());
        NavigateToObligationsCommand = new RelayCommand(() => NavigateToObligations());
        NavigateToPaymentsCommand = new RelayCommand(() => NavigateToPayments());
        NavigateToReportsCommand = new RelayCommand(() => NavigateToReports());
        NavigateToSettingsCommand = new RelayCommand(() => NavigateToSettings());
        NavigateToRulePacksCommand = new RelayCommand(() => NavigateToRulePacks());
        NavigateToSimulationCommand = new RelayCommand(() => NavigateToSimulation());
        NavigateToAuditCommand = new RelayCommand(() => NavigateToAudit());
        NavigateToChargesCommand = new RelayCommand(() => NavigateToCharges());
        NavigateToAccountsCommand = new RelayCommand(() => NavigateToAccounts());
        NavigateToCashLedgerCommand = new RelayCommand(() => NavigateToCashLedger());
        NavigateToCategoriesCommand = new RelayCommand(() => NavigateToCategories());
        NavigateToBudgetsCommand = new RelayCommand(() => NavigateToBudgets());
        NavigateToRecurringCommand = new RelayCommand(() => NavigateToRecurring());
        NavigateToImportCommand = new RelayCommand(() => NavigateToImport());
        NavigateToAssetsCommand = new RelayCommand(() => NavigateToAssets());
        NavigateToNetWorthCommand = new RelayCommand(() => NavigateToNetWorth());
        NavigateToInvestmentAccountsCommand = new RelayCommand(() => NavigateToInvestmentAccounts());
        NavigateToPortfolioInvestmentsCommand = new RelayCommand(() => NavigateToPortfolioInvestments());
        NavigateToTaxesCommand = new RelayCommand(() => NavigateToTaxes());
        NavigateToGoalsCommand = new RelayCommand(() => NavigateToGoals());
        NavigateToRetirementCommand = new RelayCommand(() => NavigateToRetirement());
        NavigateToDataQualityCommand = new RelayCommand(() => NavigateToDataQuality());
        NavigateToCurrencySettingsCommand = new RelayCommand(() => NavigateToCurrencySettings());
        NavigateToForecastCommand = new RelayCommand(() => NavigateToForecast());
        NavigateToScenariosCommand = new RelayCommand(() => NavigateToScenarios());
        NavigateToPartiesCommand = new RelayCommand(() => NavigateToParties());
        NavigateToContractsCommand = new RelayCommand(() => NavigateToContracts());
        NavigateToBillsCommand = new RelayCommand(() => NavigateToBills());
        NavigateToInvoicesCommand = new RelayCommand(() => NavigateToInvoices());
        NavigateToNotificationsCommand = new RelayCommand(() => NavigateToNotifications());
        NavigateToDocumentVaultCommand = new RelayCommand(() => NavigateToDocumentVault());
        NavigateToImportRulesCommand = new RelayCommand(() => NavigateToImportRules());
        NavigateToAiAdvisorCommand = new RelayCommand(() => NavigateToAiAdvisor());
        NavigateToIncomeSourcesCommand = new RelayCommand(() => NavigateToIncomeSources());
        NavigateToHelpCommand = new RelayCommand(() => NavigateToHelp());
        EnterSafeModeCommand = new RelayCommand(() => { IsSafeMode = true; RecoveryBannerMessage = "Safe Mode active — heavy operations disabled."; });
        ExitSafeModeCommand = new RelayCommand(() => { IsSafeMode = false; RecoveryBannerMessage = string.Empty; });
        OpenLogsFolderCommand = new RelayCommand(() => CrashRecoveryService.OpenLogsFolder());
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenAddMenuCommand = new RelayCommand<object>(OpenAddMenu);
        CreateObligationCommand = new RelayCommand(ShowCreateObligation);
        RecordPaymentCommand = new RelayCommand(ShowRecordPayment);
        DefineScheduleCommand = new RelayCommand(ShowDefineSchedule);
        CloseDialogCommand = new RelayCommand(CloseDialogs);
        FocusSearchCommand = new RelayCommand(FocusSearch);
        CloseActiveDialogCommand = new RelayCommand(CloseActiveDialog);
        SyncCommand = new AsyncRelayCommand(SyncAsync, () => !IsSyncing);

        // Default view
        CurrentView = "Dashboard";

        // Initialize sync status
        InitializeSyncStatus();

        // Listen for reload requests (e.g. after vault restore)
        if (_reloadService != null)
            _reloadService.ReloadRequested += async (_, _) => await ReloadAllAsync();
    }

    // Navigation Commands
    public ICommand NavigateToDashboardCommand { get; }

    public ICommand NavigateToObligationsCommand { get; }
    public ICommand NavigateToPaymentsCommand { get; }
    public ICommand NavigateToReportsCommand { get; }
    public ICommand NavigateToSettingsCommand { get; }
    public ICommand NavigateToRulePacksCommand { get; }
    public ICommand NavigateToSimulationCommand { get; }
    public ICommand NavigateToAuditCommand { get; }
    public ICommand NavigateToChargesCommand { get; }
    public ICommand NavigateToAccountsCommand { get; }
    public ICommand NavigateToCashLedgerCommand { get; }
    public ICommand NavigateToCategoriesCommand { get; }
    public ICommand NavigateToBudgetsCommand { get; }
    public ICommand NavigateToRecurringCommand { get; }
    public ICommand NavigateToImportCommand { get; }
    public ICommand NavigateToAssetsCommand { get; }
    public ICommand NavigateToNetWorthCommand { get; }
    public ICommand NavigateToInvestmentAccountsCommand { get; }
    public ICommand NavigateToPortfolioInvestmentsCommand { get; }
    public ICommand NavigateToTaxesCommand { get; }
    public ICommand NavigateToGoalsCommand { get; }
    public ICommand NavigateToRetirementCommand { get; }
    public ICommand NavigateToDataQualityCommand { get; }
    public ICommand NavigateToCurrencySettingsCommand { get; }
    public ICommand NavigateToForecastCommand { get; }
    public ICommand NavigateToScenariosCommand { get; }
    public ICommand NavigateToPartiesCommand { get; }
    public ICommand NavigateToContractsCommand { get; }
    public ICommand NavigateToBillsCommand { get; }
    public ICommand NavigateToInvoicesCommand { get; }
    public ICommand NavigateToNotificationsCommand { get; }
    public ICommand NavigateToDocumentVaultCommand { get; }
    public ICommand NavigateToImportRulesCommand { get; }
    public ICommand NavigateToAiAdvisorCommand { get; }
    public ICommand NavigateToIncomeSourcesCommand { get; }
    public ICommand NavigateToHelpCommand { get; }
    public ICommand EnterSafeModeCommand { get; }
    public ICommand ExitSafeModeCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }
    public ICommand OpenDataFolderCommand { get; }

    // Action Commands
    public ICommand RefreshCommand { get; }

    public ICommand OpenAddMenuCommand { get; }
    public ICommand CreateObligationCommand { get; }
    public ICommand RecordPaymentCommand { get; }
    public ICommand DefineScheduleCommand { get; }
    public ICommand CloseDialogCommand { get; }
    public ICommand FocusSearchCommand { get; }
    public ICommand CloseActiveDialogCommand { get; }
    public ICommand SyncCommand { get; }

    // Child ViewModels
    public DashboardViewModel Dashboard { get; }

    public PortfolioStatusViewModel PortfolioStatus { get; }
    public SettingsViewModel? Settings { get; }
    public ToastHostViewModel? ToastHost { get; }
    public ObligationsListViewModel ObligationsListVm { get; }
    public PaymentsListViewModel PaymentsListVm { get; }
    public ReportsViewModel? ReportsVm { get; }
    public ScenarioSimulationViewModel? SimulationVm { get; }
    public AuditTrailViewModel? AuditVm { get; }
    public ChargeBreakdownViewModel? ChargeBreakdownVm { get; }
    public AccountsViewModel? AccountsVm { get; }
    public CashLedgerViewModel? CashLedgerVm { get; }
    public CategoriesViewModel? CategoriesVm { get; }
    public BudgetsViewModel? BudgetsVm { get; }
    public RecurringViewModel? RecurringVm { get; }
    public ImportViewModel? ImportVm { get; }
    public AssetsViewModel? AssetsVm { get; }
    public NetWorthViewModel? NetWorthVm { get; }
    public InvestmentAccountsViewModel? InvestmentAccountsVm { get; }
    public PortfolioViewModel? PortfolioInvestmentsVm { get; }
    public HoldingDetailViewModel? HoldingDetailVm { get; }
    public TaxesViewModel? TaxesVm { get; }
    public GoalsViewModel? GoalsVm { get; }
    public RetirementViewModel? RetirementVm { get; }
    public DataQualityViewModel? DataQualityVm { get; }
    public CurrencySettingsViewModel? CurrencySettingsVm { get; }
    public ForecastViewModel? ForecastVm { get; }
    public ScenariosViewModel? ScenariosVm { get; }
    public PartiesViewModel? PartiesVm { get; }
    public ContractsViewModel? ContractsVm { get; }
    public BillsViewModel? BillsVm { get; }
    public InvoicesViewModel? InvoicesVm { get; }
    public NotificationsViewModel? NotificationsVm { get; }
    public DocumentVaultViewModel? DocumentVaultVm { get; }
    public ImportRulesViewModel? ImportRulesVm { get; }
    public AiAdvisorViewModel? AiAdvisorVm { get; }
    public IncomeSourcesViewModel? IncomeSourcesVm { get; }
    public HelpViewModel HelpVm { get; } = new();

    public VaultSelectorViewModel? VaultSelectorVm { get; set; }

    private string _currentVaultName = string.Empty;

    public string CurrentVaultName
    {
        get => _currentVaultName;
        set => SetProperty(ref _currentVaultName, value);
    }

    private Guid _currentVaultId;

    public Guid CurrentVaultId
    {
        get => _currentVaultId;
        set => SetProperty(ref _currentVaultId, value);
    }

    private bool _isSafeMode;

    public bool IsSafeMode
    {
        get => _isSafeMode;
        set => SetProperty(ref _isSafeMode, value);
    }

    private string _recoveryBannerMessage = string.Empty;

    public string RecoveryBannerMessage
    {
        get => _recoveryBannerMessage;
        set => SetProperty(ref _recoveryBannerMessage, value);
    }

    // Identity / Auth
    public LoginViewModel? LoginVm { get; set; }

    public UserMenuViewModel? UserMenuVm { get; set; }
    public IdentityContext? CurrentIdentityContext { get; set; }

    private bool _isAuthenticated;

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        set => SetProperty(ref _isAuthenticated, value);
    }

    private string _currentUserDisplayName = string.Empty;

    public string CurrentUserDisplayName
    {
        get => _currentUserDisplayName;
        set => SetProperty(ref _currentUserDisplayName, value);
    }

    private HashSet<string> _currentPermissions = new();

    public HashSet<string> CurrentPermissions
    {
        get => _currentPermissions;
        set => SetProperty(ref _currentPermissions, value);
    }

    private readonly GetSetupStateHandler? _getSetupStateHandler;
    private readonly CompleteInitialSetupHandler? _completeSetupHandler;
    private readonly CreateDefaultAccountsHandler? _createDefaultAccountsHandler;
    private readonly CreateDefaultCategoriesHandler? _createDefaultCategoriesHandler;
    private readonly SeedDemoDataHandler? _seedDemoHandler;

    // Navigation group expand/collapse state
    private bool _isFinanceGroupExpanded = true;

    public bool IsFinanceGroupExpanded
    {
        get => _isFinanceGroupExpanded;
        set => SetProperty(ref _isFinanceGroupExpanded, value);
    }

    private bool _isPlanningGroupExpanded = true;

    public bool IsPlanningGroupExpanded
    {
        get => _isPlanningGroupExpanded;
        set => SetProperty(ref _isPlanningGroupExpanded, value);
    }

    private bool _isInvestmentsGroupExpanded = true;

    public bool IsInvestmentsGroupExpanded
    {
        get => _isInvestmentsGroupExpanded;
        set => SetProperty(ref _isInvestmentsGroupExpanded, value);
    }

    private bool _isAdminGroupExpanded = true;

    public bool IsAdminGroupExpanded
    {
        get => _isAdminGroupExpanded;
        set => SetProperty(ref _isAdminGroupExpanded, value);
    }

    private bool _isAddMenuOpen;

    public bool IsAddMenuOpen
    {
        get => _isAddMenuOpen;
        set => SetProperty(ref _isAddMenuOpen, value);
    }

    private RulePackManagerViewModel? _rulePackManagerVm;

    public RulePackManagerViewModel? RulePackManagerVm
    {
        get => _rulePackManagerVm;
        set => SetProperty(ref _rulePackManagerVm, value);
    }

    private OnboardingViewModel? _onboardingVm;

    public OnboardingViewModel? OnboardingVm
    {
        get => _onboardingVm;
        set => SetProperty(ref _onboardingVm, value);
    }

    private bool _isOnboardingVisible;

    public bool IsOnboardingVisible
    {
        get => _isOnboardingVisible;
        set => SetProperty(ref _isOnboardingVisible, value);
    }

    private InitialSetupViewModel? _initialSetupVm;

    public InitialSetupViewModel? InitialSetupVm
    {
        get => _initialSetupVm;
        set => SetProperty(ref _initialSetupVm, value);
    }

    private bool _isInitialSetupVisible;

    public bool IsInitialSetupVisible
    {
        get => _isInitialSetupVisible;
        set => SetProperty(ref _isInitialSetupVisible, value);
    }

    private CreateObligationViewModel? _createObligationVm;

    public CreateObligationViewModel? CreateObligationVm
    {
        get => _createObligationVm;
        set => SetProperty(ref _createObligationVm, value);
    }

    private RecordPaymentViewModel? _recordPaymentVm;

    public RecordPaymentViewModel? RecordPaymentVm
    {
        get => _recordPaymentVm;
        set => SetProperty(ref _recordPaymentVm, value);
    }

    private DefineScheduleViewModel? _defineScheduleVm;

    public DefineScheduleViewModel? DefineScheduleVm
    {
        get => _defineScheduleVm;
        set => SetProperty(ref _defineScheduleVm, value);
    }

    private ObligationDetailViewModel? _obligationDetailVm;

    public ObligationDetailViewModel? ObligationDetailVm
    {
        get => _obligationDetailVm;
        set => SetProperty(ref _obligationDetailVm, value);
    }

    // Selected obligation ID for detail view
    private Guid? _selectedObligationId;

    public Guid? SelectedObligationId
    {
        get => _selectedObligationId;
        set => SetProperty(ref _selectedObligationId, value);
    }

    // Current view name
    private string _currentView = "Dashboard";

    public string CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    // Status
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

    private bool _isSyncing;

    public bool IsSyncing
    {
        get => _isSyncing;
        set => SetProperty(ref _isSyncing, value);
    }

    private DateTimeOffset? _lastSyncTime;

    public DateTimeOffset? LastSyncTime
    {
        get => _lastSyncTime;
        set
        {
            if (SetProperty(ref _lastSyncTime, value))
            {
                OnPropertyChanged(nameof(LastSyncDisplay));
            }
        }
    }

    public string LastSyncDisplay => LastSyncTime.HasValue
        ? $"Last sync: {LastSyncTime.Value:yyyy-MM-dd HH:mm}"
        : "Never synced";

    private string _syncStatusMessage = "Ready to sync";

    public string SyncStatusMessage
    {
        get => _syncStatusMessage;
        set => SetProperty(ref _syncStatusMessage, value);
    }

    private bool _isSyncConfigured;

    public bool IsSyncConfigured
    {
        get => _isSyncConfigured;
        set => SetProperty(ref _isSyncConfigured, value);
    }

    // Dialog visibility
    private bool _showCreateObligationDialog;

    public bool ShowCreateObligationDialog
    {
        get => _showCreateObligationDialog;
        set => SetProperty(ref _showCreateObligationDialog, value);
    }

    private bool _showRecordPaymentDialog;

    public bool ShowRecordPaymentDialog
    {
        get => _showRecordPaymentDialog;
        set => SetProperty(ref _showRecordPaymentDialog, value);
    }

    private bool _showDefineScheduleDialog;

    public bool ShowDefineScheduleDialog
    {
        get => _showDefineScheduleDialog;
        set => SetProperty(ref _showDefineScheduleDialog, value);
    }

    public async Task InitializeAsync()
    {
        await LogSecurityEventAsync(SecurityEventType.ApplicationStarted, "Application started");

        // Check for crash recovery
        var recovery = new CrashRecoveryService();
        var crashMarker = recovery.DetectCrashMarker();
        if (crashMarker != null)
        {
            var decision = recovery.Evaluate(crashMarker);
            if (decision == RecoveryDecision.SafeMode)
            {
                IsSafeMode = true;
                RecoveryBannerMessage = $"Previous session did not exit cleanly (ID: {crashMarker.CorrelationId}). Safe Mode active — heavy operations disabled.";
                AppDiagnostics.WriteWarn("Recovery", $"Crash marker detected, entering safe mode. CID: {crashMarker.CorrelationId}");
            }
        }

        // Check first-run: show onboarding if not completed
        if (_secureConfiguration != null)
        {
            var completed = _secureConfiguration.Get(ConfigKeys.HasCompletedOnboarding);
            if (!string.Equals(completed, bool.TrueString, StringComparison.OrdinalIgnoreCase))
            {
                StartOnboarding();
                return;
            }
        }

        await RefreshAsync();

        // After onboarding check, also check if initial setup wizard is needed
        if (_getSetupStateHandler != null)
        {
            try
            {
                var setupState = await _getSetupStateHandler.HandleAsync(CancellationToken.None);
                if (!setupState.IsInitialSetupCompleted)
                {
                    StartInitialSetup();
                }
            }
            catch { /* proceed to dashboard */ }
        }
    }

    /// <summary>
    /// Starts the onboarding wizard. Can be called on first run or from Settings.
    /// </summary>
    public void StartOnboarding()
    {
        if (_secureConfiguration == null)
            return;

        OnboardingVm = new OnboardingViewModel(
            _secureConfiguration,
            _deviceId,
            onCompleted: OnOnboardingCompleted,
            toastService: _toastService);

        IsOnboardingVisible = true;
    }

    private async void OnOnboardingCompleted()
    {
        IsOnboardingVisible = false;
        OnboardingVm = null;

        // Re-check sync configuration after onboarding may have set sync keys
        OnSyncConfigChanged();

        // Reload settings view if it exists
        Settings?.LoadSettings();

        // Navigate to dashboard and refresh
        CurrentView = "Dashboard";
        await RefreshAsync();

        // After onboarding, check if initial setup is needed
        if (_getSetupStateHandler != null)
        {
            try
            {
                var setupState = await _getSetupStateHandler.HandleAsync(CancellationToken.None);
                if (!setupState.IsInitialSetupCompleted)
                {
                    StartInitialSetup();
                }
            }
            catch { /* proceed to dashboard */ }
        }
    }

    public void StartInitialSetup()
    {
        if (_getSetupStateHandler == null || _completeSetupHandler == null ||
            _createDefaultAccountsHandler == null || _createDefaultCategoriesHandler == null ||
            _seedDemoHandler == null)
            return;

        InitialSetupVm = new InitialSetupViewModel(
            _getSetupStateHandler,
            _completeSetupHandler,
            _createDefaultAccountsHandler,
            _createDefaultCategoriesHandler,
            _seedDemoHandler,
            _actorUserId,
            _deviceId,
            onCompleted: OnInitialSetupCompleted,
            toastService: _toastService);

        IsInitialSetupVisible = true;
    }

    private async void OnInitialSetupCompleted()
    {
        IsInitialSetupVisible = false;
        InitialSetupVm = null;

        _reloadService?.RequestReload();

        CurrentView = "Dashboard";
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        IsLoading = true;
        StatusMessage = "Refreshing...";

        try
        {
            var asOfDate = DateOnly.FromDateTime(DateTime.Today);

            await Dashboard.RefreshAsync();
            await PortfolioStatus.RefreshAsync(asOfDate, CancellationToken.None);

            // Also refresh obligation detail if visible
            if (CurrentView == "ObligationDetail" && ObligationDetailVm != null)
            {
                await ObligationDetailVm.LoadAsync();
            }

            StatusMessage = $"Last updated: {DateTime.Now:HH:mm:ss}";
            AdvanceGlobalWatermark();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ReloadAllAsync()
    {
        _viewWatermarks.Clear();
        _globalEventWatermark = DateTimeOffset.MinValue;
        try
        {
            await Dashboard.RefreshAsync();
            var asOfDate = DateOnly.FromDateTime(DateTime.Today);
            await PortfolioStatus.RefreshAsync(asOfDate, CancellationToken.None);

            if (ObligationsListVm != null) _ = ObligationsListVm.LoadAsync();
            if (PaymentsListVm != null) _ = PaymentsListVm.LoadAsync();
            if (AuditVm != null) _ = AuditVm.LoadAsync();
            if (ChargeBreakdownVm != null) _ = ChargeBreakdownVm.LoadObligationsAsync();
            if (RulePackManagerVm != null) _ = RulePackManagerVm.LoadAsync();
            if (AccountsVm != null) _ = AccountsVm.LoadAsync();
            if (CashLedgerVm != null) _ = CashLedgerVm.LoadAsync();
            if (CategoriesVm != null) _ = CategoriesVm.LoadAsync();
            if (BudgetsVm != null) _ = BudgetsVm.LoadAsync();
            if (RecurringVm != null) _ = RecurringVm.LoadAsync();
            if (ImportVm != null) _ = ImportVm.LoadAsync();
            if (AssetsVm != null) _ = AssetsVm.LoadAsync();
            if (NetWorthVm != null) _ = NetWorthVm.LoadAsync();
            if (InvestmentAccountsVm != null) _ = InvestmentAccountsVm.LoadAsync();
            if (PortfolioInvestmentsVm != null) _ = PortfolioInvestmentsVm.LoadAsync();
            if (TaxesVm != null) _ = TaxesVm.LoadAsync();
            if (GoalsVm != null) _ = GoalsVm.LoadAsync();
            if (RetirementVm != null) _ = RetirementVm.LoadAsync();
            if (DataQualityVm != null) _ = DataQualityVm.LoadAsync();
            if (CurrencySettingsVm != null) _ = CurrencySettingsVm.LoadAsync();
            if (ForecastVm != null) _ = ForecastVm.LoadAsync();
            if (ScenariosVm != null) _ = ScenariosVm.LoadAsync();
            if (PartiesVm != null) _ = PartiesVm.LoadAsync();
            if (ContractsVm != null) _ = ContractsVm.LoadAsync();
            if (BillsVm != null) _ = BillsVm.LoadAsync();
            if (InvoicesVm != null) _ = InvoicesVm.LoadAsync();
            if (NotificationsVm != null) _ = NotificationsVm.LoadAsync();
            if (DocumentVaultVm != null) _ = DocumentVaultVm.LoadAsync();
            if (ImportRulesVm != null) _ = ImportRulesVm.LoadAsync();
            if (AiAdvisorVm != null) _ = AiAdvisorVm.LoadAsync();
            if (IncomeSourcesVm != null) _ = IncomeSourcesVm.LoadAsync();
            if (ReportsVm != null) _ = ReportsVm.LoadAsync();

            Settings?.LoadSettings();

            CurrentView = "Dashboard";
            StatusMessage = $"Vault reloaded: {DateTime.Now:HH:mm:ss}";
            AdvanceGlobalWatermark();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Reload failed: {ex.Message}";
        }
    }

    private void NavigateToDashboard()
    {
        CurrentView = "Dashboard";
        ObligationDetailVm = null;
        SelectedObligationId = null;
    }

    private void NavigateToSettings()
    {
        CurrentView = "Settings";
        // Reload settings when navigating to ensure fresh data
        Settings?.LoadSettings();
    }

    private void NavigateToRulePacks()
    {
        CurrentView = "RulePacks";
        // Refresh rule pack data when navigating
        _ = RulePackManagerVm?.LoadAsync();
    }

    private void NavigateToObligations()
    {
        CurrentView = "Obligations";
        if (ShouldRefreshView("Obligations"))
            _ = ObligationsListVm?.LoadAsync();
    }

    private void NavigateToPayments()
    {
        CurrentView = "Payments";
        if (ShouldRefreshView("Payments"))
            _ = PaymentsListVm?.LoadAsync();
    }

    private void NavigateToSimulation()
    {
        CurrentView = "Simulation";
        if (ShouldRefreshView("Simulation"))
            _ = SimulationVm?.LoadObligationsAsync();
    }

    private void NavigateToAudit()
    {
        CurrentView = "Audit";
        if (ShouldRefreshView("Audit"))
            _ = AuditVm?.LoadAsync();
    }

    private void NavigateToCharges()
    {
        CurrentView = "Charges";
        if (ShouldRefreshView("Charges"))
            _ = ChargeBreakdownVm?.LoadObligationsAsync();
    }

    private void NavigateToAccounts()
    {
        CurrentView = "Accounts";
        if (ShouldRefreshView("Accounts"))
            _ = AccountsVm?.LoadAsync();
    }

    private void NavigateToCashLedger()
    {
        CurrentView = "CashLedger";
        if (ShouldRefreshView("CashLedger"))
            _ = CashLedgerVm?.LoadAsync();
    }

    private void NavigateToCategories()
    {
        CurrentView = "Categories";
        if (ShouldRefreshView("Categories"))
            _ = CategoriesVm?.LoadAsync();
    }

    private void NavigateToBudgets()
    {
        CurrentView = "Budgets";
        if (ShouldRefreshView("Budgets"))
            _ = BudgetsVm?.LoadAsync();
    }

    private void NavigateToRecurring()
    {
        CurrentView = "Recurring";
        if (ShouldRefreshView("Recurring"))
            _ = RecurringVm?.LoadAsync();
    }

    private void NavigateToImport()
    {
        CurrentView = "Import";
        if (ShouldRefreshView("Import"))
            _ = ImportVm?.LoadAsync();
    }

    private void NavigateToAssets()
    {
        CurrentView = "Assets2";
        if (ShouldRefreshView("Assets2"))
            _ = AssetsVm?.LoadAsync();
    }

    private void NavigateToNetWorth()
    {
        CurrentView = "NetWorth";
        if (ShouldRefreshView("NetWorth"))
            _ = NetWorthVm?.LoadAsync();
    }

    private void NavigateToInvestmentAccounts()
    {
        CurrentView = "InvestmentAccounts";
        if (ShouldRefreshView("InvestmentAccounts"))
            _ = InvestmentAccountsVm?.LoadAsync();
    }

    private void NavigateToPortfolioInvestments()
    {
        CurrentView = "PortfolioInvestments";
        if (ShouldRefreshView("PortfolioInvestments"))
            _ = PortfolioInvestmentsVm?.LoadAsync();
    }

    private void NavigateToTaxes()
    {
        CurrentView = "Taxes";
        if (ShouldRefreshView("Taxes"))
            _ = TaxesVm?.LoadAsync();
    }

    private void NavigateToGoals()
    {
        CurrentView = "Goals";
        if (ShouldRefreshView("Goals"))
            _ = GoalsVm?.LoadAsync();
    }

    private void NavigateToRetirement()
    {
        CurrentView = "Retirement";
        if (ShouldRefreshView("Retirement"))
            _ = RetirementVm?.LoadAsync();
    }

    private void NavigateToDataQuality()
    {
        CurrentView = "DataQuality";
        if (ShouldRefreshView("DataQuality"))
            _ = DataQualityVm?.LoadAsync();
    }

    private void NavigateToCurrencySettings()
    {
        CurrentView = "CurrencySettings";
        if (ShouldRefreshView("CurrencySettings"))
            _ = CurrencySettingsVm?.LoadAsync();
    }

    private void NavigateToForecast()
    {
        CurrentView = "Forecast";
        if (ShouldRefreshView("Forecast"))
            _ = ForecastVm?.LoadAsync();
    }

    private void NavigateToScenarios()
    {
        CurrentView = "Scenarios";
        if (ShouldRefreshView("Scenarios"))
            _ = ScenariosVm?.LoadAsync();
    }

    private void NavigateToParties()
    {
        CurrentView = "Parties";
        if (ShouldRefreshView("Parties"))
            _ = PartiesVm?.LoadAsync();
    }

    private void NavigateToContracts()
    {
        CurrentView = "Contracts";
        if (ShouldRefreshView("Contracts"))
            _ = ContractsVm?.LoadAsync();
    }

    private void NavigateToBills()
    {
        CurrentView = "Bills";
        if (ShouldRefreshView("Bills"))
            _ = BillsVm?.LoadAsync();
    }

    private void NavigateToInvoices()
    {
        CurrentView = "Invoices";
        if (ShouldRefreshView("Invoices"))
            _ = InvoicesVm?.LoadAsync();
    }

    private void NavigateToNotifications()
    {
        CurrentView = "Notifications";
        if (ShouldRefreshView("Notifications"))
            _ = NotificationsVm?.LoadAsync();
    }

    private void NavigateToDocumentVault()
    {
        CurrentView = "DocumentVault";
        if (ShouldRefreshView("DocumentVault"))
            _ = DocumentVaultVm?.LoadAsync();
    }

    private void NavigateToImportRules()
    {
        CurrentView = "ImportRules";
        if (ShouldRefreshView("ImportRules"))
            _ = ImportRulesVm?.LoadAsync();
    }

    private void NavigateToAiAdvisor()
    {
        CurrentView = "AiAdvisor";
        if (ShouldRefreshView("AiAdvisor"))
            _ = AiAdvisorVm?.LoadAsync();
    }

    private void NavigateToIncomeSources()
    {
        CurrentView = "IncomeSources";
        if (ShouldRefreshView("IncomeSources"))
            _ = IncomeSourcesVm?.LoadAsync();
    }

    private void NavigateToReports()
    {
        CurrentView = "Reports";
        if (ShouldRefreshView("Reports"))
            _ = ReportsVm?.LoadAsync();
    }

    private void NavigateToHelp()
    {
        CurrentView = "Help";
    }

    private static void OpenDataFolder()
    {
        try
        {
            var dataDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DebtManager");
            if (System.IO.Directory.Exists(dataDir))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dataDir,
                    UseShellExecute = true
                });
        }
        catch { /* best-effort */ }
    }

    private void OnSyncConfigChanged()
    {
        // Re-check sync configuration after settings are saved
        if (_secureConfiguration != null)
        {
            var baseUrl = _secureConfiguration.Get(ConfigKeys.SyncBaseUrl);
            var apiKey = _secureConfiguration.Get(ConfigKeys.SyncApiKey);

            if (!string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(apiKey))
            {
                IsSyncConfigured = true;
                SyncStatusMessage = "Ready to sync";
            }
            else
            {
                IsSyncConfigured = false;
                SyncStatusMessage = "Sync not configured";
            }
        }
    }

    /// <summary>
    /// Navigate to obligation detail view.
    /// Called from DashboardViewModel when user clicks an obligation.
    /// </summary>
    public void NavigateToObligationDetail(Guid obligationId)
    {
        if (_snapshotHandler == null || _closeObligationHandler == null)
            return;

        SelectedObligationId = obligationId;

        ObligationDetailVm = new ObligationDetailViewModel(
            _snapshotHandler,
            _closeObligationHandler,
            _actorUserId,
            _deviceId,
            onBack: NavigateToDashboard,
            onRecordPayment: ShowRecordPaymentForObligation,
            onDefineSchedule: ShowDefineScheduleForObligation,
            onRefreshDashboard: async () => await Dashboard.RefreshAsync(),
            toastService: _toastService,
            ledgerHandler: _ledgerHandler,
            reverseHandler: _reversePaymentHandler
        );

        ObligationDetailVm.ObligationId = obligationId;
        _ = ObligationDetailVm.LoadAsync();

        CurrentView = "ObligationDetail";
    }

    /// <summary>
    /// Show record payment dialog for a specific obligation (from obligations list).
    /// </summary>
    public void ShowRecordPaymentForObligationFromList(Guid obligationId)
    {
        ShowRecordPaymentForObligation(obligationId);
    }

    /// <summary>
    /// Show define schedule dialog for a specific obligation (from obligations list).
    /// </summary>
    public void ShowDefineScheduleForObligationFromList(Guid obligationId)
    {
        ShowDefineScheduleForObligation(obligationId);
    }

    private void ShowRecordPaymentForObligation(Guid obligationId)
    {
        if (_recordPaymentHandler == null)
            return;

        RecordPaymentVm = new RecordPaymentViewModel(
            _recordPaymentHandler,
            _eventStore,
            _actorUserId,
            _deviceId,
            onSuccess: async () =>
            {
                ShowRecordPaymentDialog = false;
                RecordPaymentVm = null;
                _toastService?.Success("Payment recorded");
                await RefreshAsync();
                await LogSecurityEventAsync(SecurityEventType.PaymentRecorded, "Payment recorded");
            },
            onCancel: () =>
            {
                ShowRecordPaymentDialog = false;
                RecordPaymentVm = null;
            },
            config: _secureConfiguration,
            toastService: _toastService,
            previewHandler: _previewPaymentHandler);

        // Load obligations and pre-select the specified one
        _ = RecordPaymentVm.LoadObligationsAsync().ContinueWith(_ =>
        {
            var selected = RecordPaymentVm.Obligations.FirstOrDefault(o => o.Id == obligationId);
            if (selected != null)
                RecordPaymentVm.SelectedObligation = selected;
        }, TaskScheduler.FromCurrentSynchronizationContext());

        ShowRecordPaymentDialog = true;
    }

    private void ShowDefineScheduleForObligation(Guid obligationId)
    {
        if (_defineScheduleHandler == null)
            return;

        DefineScheduleVm = new DefineScheduleViewModel(
            _defineScheduleHandler,
            _eventStore,
            _actorUserId,
            _deviceId,
            onSuccess: async () =>
            {
                ShowDefineScheduleDialog = false;
                DefineScheduleVm = null;
                _toastService?.Success("Schedule defined");
                await RefreshAsync();
            },
            onCancel: () =>
            {
                ShowDefineScheduleDialog = false;
                DefineScheduleVm = null;
            },
            config: _secureConfiguration,
            toastService: _toastService);

        // Load obligations and pre-select the specified one
        _ = DefineScheduleVm.LoadObligationsAsync().ContinueWith(_ =>
        {
            var selected = DefineScheduleVm.Obligations.FirstOrDefault(o => o.Id == obligationId);
            if (selected != null)
                DefineScheduleVm.SelectedObligation = selected;
        }, TaskScheduler.FromCurrentSynchronizationContext());

        ShowDefineScheduleDialog = true;
    }

    private void ShowCreateObligation()
    {
        if (_createObligationHandler != null)
        {
            CreateObligationVm = new CreateObligationViewModel(
                _createObligationHandler,
                _actorUserId,
                _deviceId,
                onSuccess: async () =>
                {
                    ShowCreateObligationDialog = false;
                    CreateObligationVm = null;
                    _toastService?.Success("Obligation created");
                    await RefreshAsync();
                    await LogSecurityEventAsync(SecurityEventType.ObligationCreated, "New obligation created");
                },
                onCancel: () =>
                {
                    ShowCreateObligationDialog = false;
                    CreateObligationVm = null;
                },
                toastService: _toastService);
        }
        ShowCreateObligationDialog = true;
    }

    private void ShowRecordPayment()
    {
        if (_recordPaymentHandler != null)
        {
            RecordPaymentVm = new RecordPaymentViewModel(
                _recordPaymentHandler,
                _eventStore,
                _actorUserId,
                _deviceId,
                onSuccess: async () =>
                {
                    ShowRecordPaymentDialog = false;
                    RecordPaymentVm = null;
                    _toastService?.Success("Payment recorded");
                    await RefreshAsync();
                    await LogSecurityEventAsync(SecurityEventType.PaymentRecorded, "Payment recorded");
                },
                onCancel: () =>
                {
                    ShowRecordPaymentDialog = false;
                    RecordPaymentVm = null;
                },
                config: _secureConfiguration,
                toastService: _toastService,
                previewHandler: _previewPaymentHandler);

            // Load obligations for the dropdown
            _ = RecordPaymentVm.LoadObligationsAsync();
        }
        ShowRecordPaymentDialog = true;
    }

    private void ShowDefineSchedule()
    {
        if (_defineScheduleHandler != null)
        {
            DefineScheduleVm = new DefineScheduleViewModel(
                _defineScheduleHandler,
                _eventStore,
                _actorUserId,
                _deviceId,
                onSuccess: async () =>
                {
                    ShowDefineScheduleDialog = false;
                    DefineScheduleVm = null;
                    _toastService?.Success("Schedule defined");
                    await RefreshAsync();
                },
                onCancel: () =>
                {
                    ShowDefineScheduleDialog = false;
                    DefineScheduleVm = null;
                },
                config: _secureConfiguration,
                toastService: _toastService);

            // Load obligations for the dropdown
            _ = DefineScheduleVm.LoadObligationsAsync();
        }
        ShowDefineScheduleDialog = true;
    }

    private void CloseDialogs()
    {
        ShowCreateObligationDialog = false;
        ShowRecordPaymentDialog = false;
        ShowDefineScheduleDialog = false;
        CreateObligationVm = null;
        RecordPaymentVm = null;
        DefineScheduleVm = null;
    }

    private void FocusSearch()
    {
        if (_focusService == null)
            return;

        switch (CurrentView)
        {
            case "Obligations":
                _focusService.RequestFocus(FocusTargets.ObligationsSearch);
                break;

            case "Payments":
                _focusService.RequestFocus(FocusTargets.PaymentsSearch);
                break;

            case "Audit":
                _focusService.RequestFocus(FocusTargets.AuditSearch);
                break;
        }
    }

    private void CloseActiveDialog()
    {
        if (ShowCreateObligationDialog)
        {
            ShowCreateObligationDialog = false;
            CreateObligationVm = null;
        }
        else if (ShowRecordPaymentDialog)
        {
            ShowRecordPaymentDialog = false;
            RecordPaymentVm = null;
        }
        else if (ShowDefineScheduleDialog)
        {
            ShowDefineScheduleDialog = false;
            DefineScheduleVm = null;
        }
        else if (CurrentView == "Settings")
        {
            NavigateToDashboard();
        }
    }

    private void InitializeSyncStatus()
    {
        if (_syncEngine == null || string.IsNullOrEmpty(_vaultId))
        {
            IsSyncConfigured = false;
            SyncStatusMessage = "Sync not configured";
        }
        else
        {
            IsSyncConfigured = true;
            SyncStatusMessage = "Ready to sync";
        }
    }

    private async Task SyncAsync()
    {
        if (IsSyncing) return;

        // Check if sync is configured
        if (_syncEngine == null || string.IsNullOrEmpty(_vaultId))
        {
            SyncStatusMessage = "Sync not configured";
            return;
        }

        // Cancel any existing sync
        _syncCts?.Cancel();
        _syncCts = new CancellationTokenSource();
        var ct = _syncCts.Token;

        IsSyncing = true;
        SyncStatusMessage = "Syncing...";

        try
        {
            await LogSecurityEventAsync(SecurityEventType.SyncPushStarted, "Manual sync initiated");

            // Retry with exponential backoff for transient errors
            var maxRetries = 3;
            var baseDelay = TimeSpan.FromSeconds(1);
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    SyncStatusMessage = attempt > 1
                        ? $"Syncing... (attempt {attempt}/{maxRetries})"
                        : "Syncing...";

                    await _syncEngine.SyncOnceAsync(_vaultId, _deviceId, ct);

                    // Success!
                    LastSyncTime = DateTimeOffset.Now;
                    SyncStatusMessage = "Sync successful";
                    _toastService?.Success("Sync completed successfully");
                    await LogSecurityEventAsync(SecurityEventType.SyncPushCompleted, "Sync completed successfully");

                    // Refresh dashboard after successful sync
                    await Dashboard.RefreshAsync();
                    AdvanceGlobalWatermark();
                    return;
                }
                catch (OperationCanceledException)
                {
                    SyncStatusMessage = "Sync cancelled";
                    return;
                }
                catch (HttpRequestException ex)
                {
                    // Network error - retry
                    lastException = ex;
                    if (attempt < maxRetries)
                    {
                        var delay = baseDelay * Math.Pow(2, attempt - 1);
                        SyncStatusMessage = $"Connection error. Retrying in {delay.TotalSeconds:0}s...";
                        await Task.Delay(delay, ct);
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    // Auth failure - don't retry
                    SyncStatusMessage = "Sync failed: Authentication error";
                    _toastService?.Error("Sync failed: Authentication error");
                    await LogSecurityEventAsync(SecurityEventType.SyncPushFailed, $"Auth failure: {ex.Message}", success: false);
                    return;
                }
                catch (Exception ex) when (IsTransientError(ex))
                {
                    // Transient error - retry
                    lastException = ex;
                    if (attempt < maxRetries)
                    {
                        var delay = baseDelay * Math.Pow(2, attempt - 1);
                        SyncStatusMessage = $"Temporary error. Retrying in {delay.TotalSeconds:0}s...";
                        await Task.Delay(delay, ct);
                    }
                }
                catch (Exception ex)
                {
                    // Non-transient error - don't retry
                    var shortMessage = ex.Message.Length > 50 ? ex.Message[..47] + "..." : ex.Message;
                    SyncStatusMessage = $"Sync failed: {shortMessage}";
                    _toastService?.Error($"Sync failed: {shortMessage}");
                    await LogSecurityEventAsync(SecurityEventType.SyncPushFailed, $"Sync failed: {ex.Message}", success: false);
                    return;
                }
            }

            // All retries exhausted
            var errorMsg = lastException?.Message ?? "Unknown error";
            var shortError = errorMsg.Length > 50 ? errorMsg[..47] + "..." : errorMsg;
            SyncStatusMessage = $"Sync failed: {shortError}";
            _toastService?.Error($"Sync failed after {maxRetries} attempts");
            await LogSecurityEventAsync(SecurityEventType.SyncPushFailed, $"Sync failed after {maxRetries} attempts: {errorMsg}", success: false);
        }
        catch (Exception ex)
        {
            var shortMessage = ex.Message.Length > 50 ? ex.Message[..47] + "..." : ex.Message;
            SyncStatusMessage = $"Sync failed: {shortMessage}";
            _toastService?.Error($"Sync failed: {shortMessage}");
            await LogSecurityEventAsync(SecurityEventType.SyncPushFailed, $"Sync failed: {ex.Message}", success: false);
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private static bool IsTransientError(Exception ex)
    {
        // Check for common transient error patterns
        return ex is TimeoutException
            || ex is TaskCanceledException
            || (ex.InnerException is HttpRequestException);
    }

    private async Task LogSecurityEventAsync(SecurityEventType type, string message, bool success = true)
    {
        if (_auditLogger == null) return;

        var entry = SecurityAudit.Create(
            type: type,
            message: message,
            success: success,
            userId: _actorUserId.ToString(),
            deviceId: _deviceId.ToString()
        );

        try
        {
            await _auditLogger.LogAsync(entry);
        }
        catch
        {
            // Don't fail on audit logging issues
        }
    }

    /// <summary>
    /// Determines whether a view should be refreshed on navigation.
    /// Always-refresh views (Dashboard, CashLedger, Portfolio, NetWorth, Budgets) always reload.
    /// Other views only reload if new events have been appended since their last load.
    /// </summary>
    private bool ShouldRefreshView(string viewName)
    {
        if (_alwaysRefreshViews.Contains(viewName))
        {
            _viewWatermarks[viewName] = _globalEventWatermark;
            return true;
        }

        if (!_viewWatermarks.TryGetValue(viewName, out var lastWatermark))
        {
            _viewWatermarks[viewName] = _globalEventWatermark;
            return true;
        }

        if (_globalEventWatermark > lastWatermark)
        {
            _viewWatermarks[viewName] = _globalEventWatermark;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Updates the global event watermark. Called after any mutation that appends events.
    /// </summary>
    private void AdvanceGlobalWatermark()
    {
        _globalEventWatermark = DateTimeOffset.UtcNow;
    }

    private void OpenAddMenu(object? parameter)
    {
        if (parameter is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }
    }
}