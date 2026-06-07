using DebtManager.Application.Fx;
using DebtManager.Application.Identity;
using DebtManager.Application.Projections;
using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Infrastructure.Identity;
using DebtManager.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Net.Http;
using DebtManager.Infrastructure.Rules;
using DebtManager.Domain.Rules;
using DebtManager.Application.Simulation;
using DebtManager.Infrastructure.Security;
using DebtManager.Infrastructure.Documents;
using DebtManager.Desktop.Security;
using DebtManager.Desktop.ViewModels;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections.Snapshots;
using DebtManager.Infrastructure.Sync;
using DebtManager.Sync;
using DebtManager.Sync.Transport;
using DebtManager.Domain.Vault;
using DebtManager.Infrastructure.Vault;

namespace DebtManager.Desktop.Bootstrap;

public static class AppHost
{
    /// <summary>
    /// Resolves vault paths and runs migration if needed. Returns active vault ID.
    /// </summary>
    public static async Task<(Guid VaultId, VaultPaths Paths, VaultRegistry Registry, GlobalEventStore GlobalStore)> ResolveVaultAsync()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataDir = Path.Combine(appData, "DebtManager");
        Directory.CreateDirectory(dataDir);

        var registryStore = new DpapiProtectedRegistryStore();
        var globalStore = new GlobalEventStore();
        var registry = new VaultRegistry(registryStore, globalStore);

        // Migration: detect legacy single-vault layout
        var migratedId = await VaultMigration.MigrateIfNeededAsync(dataDir, registry, globalStore);

        var manifest = await registry.LoadManifestAsync();

        Guid activeVaultId;
        if (manifest.Vaults.Count == 0)
        {
            // Fresh install: create default vault
            var desc = await registry.CreateVaultAsync("Default", "EGP");
            await registry.SetActiveVaultAsync(desc.VaultId);
            activeVaultId = desc.VaultId;
        }
        else if (manifest.ActiveVaultId.HasValue)
        {
            activeVaultId = manifest.ActiveVaultId.Value;
        }
        else
        {
            // No active vault, pick the first non-archived
            var first = manifest.Vaults.FirstOrDefault(v => !v.IsArchived)
                ?? throw new InvalidOperationException("No active vaults available.");
            await registry.SetActiveVaultAsync(first.VaultId);
            activeVaultId = first.VaultId;
        }

        var paths = registry.ResolveVaultPaths(activeVaultId);
        return (activeVaultId, paths, registry, globalStore);
    }

    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddLogging(b =>
        {
            b.ClearProviders();
            b.AddConsole();
            b.SetMinimumLevel(LogLevel.Information);
        });

        // Local DB path (AppData)
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataDir = Path.Combine(appData, "DebtManager");
        Directory.CreateDirectory(dataDir);

        var dbPath = Path.Combine(dataDir, "debtmanager_local.db");

        // Security
        services.AddSingleton<IKeyStore, DpapiKeyStore>();
        services.AddSingleton<DeviceIdentityProvider>();

        // Global vault services
        services.AddSingleton<DpapiProtectedRegistryStore>();
        services.AddSingleton<GlobalEventStore>();
        services.AddSingleton(sp => new VaultRegistry(
            sp.GetRequiredService<DpapiProtectedRegistryStore>(),
            sp.GetRequiredService<GlobalEventStore>()));

        // Infrastructure - Connection Factory
        services.AddSingleton(sp =>
        {
            var keys = sp.GetRequiredService<IKeyStore>();
            return new SqliteConnectionFactory(dbPath, keys);
        });

        // Infrastructure - Event Store (also implements ISyncStore)
        services.AddSingleton<SqliteEventStore>();
        services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<SqliteEventStore>());
        services.AddSingleton<ISyncStore>(sp => sp.GetRequiredService<SqliteEventStore>());

        // Infrastructure - Security Audit
        services.AddSingleton<ISecurityAuditLogger>(sp =>
        {
            var factory = sp.GetRequiredService<SqliteConnectionFactory>();
            return new SqliteSecurityAuditLogger(factory);
        });

        // Infrastructure - Secure Configuration
        services.AddSingleton(sp =>
        {
            var keys = sp.GetRequiredService<IKeyStore>();
            return new SecureConfiguration(keys);
        });

        // Sync Transport - select based on configuration
        services.AddSingleton<ISyncTransport>(sp =>
        {
            var config = sp.GetRequiredService<SecureConfiguration>();
            var baseUrl = config.Get(ConfigKeys.SyncBaseUrl);
            var apiKey = config.Get(ConfigKeys.SyncApiKey);

            if (!string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(apiKey))
            {
                // Use secure Azure transport if configured
                var device = sp.GetRequiredService<DeviceIdentityProvider>();
                var keys = sp.GetRequiredService<IKeyStore>();
                var encryptor = new PayloadEncryptor(keys);
                var auditLogger = sp.GetRequiredService<ISecurityAuditLogger>();
                var httpClient = new HttpClient();

                return new SecureAzureSyncTransport(
                    httpClient,
                    baseUrl,
                    apiKey,
                    device.GetOrCreateDeviceId().ToString(),
                    encryptor,
                    auditLogger);
            }

            // Fall back to in-memory transport for local testing
            return new InMemorySyncTransport();
        });

        // Sync Engine
        services.AddSingleton<SyncEngine>(sp =>
        {
            var store = sp.GetRequiredService<ISyncStore>();
            var transport = sp.GetRequiredService<ISyncTransport>();
            return new SyncEngine(store, transport);
        });

        // Rule Engine
        services.AddSingleton<IRulePackRepository, SqliteRulePackRepository>();
        services.AddSingleton<IRulePackResolver, SqliteRulePackResolver>();
        services.AddSingleton<IRuleEngine, SqliteRuleEngine>();

        // Projection Snapshot Store + Cache + Runner
        services.AddSingleton<IProjectionSnapshotStore, SqliteProjectionSnapshotStore>();
        services.AddSingleton<ProjectionCache>();
        services.AddSingleton<ProjectionRunner>(sp =>
        {
            var store = sp.GetRequiredService<IEventStore>();
            var snapshotStore = sp.GetRequiredService<IProjectionSnapshotStore>();
            var cache = sp.GetRequiredService<ProjectionCache>();
            var device = sp.GetRequiredService<DeviceIdentityProvider>();
            return new ProjectionRunner(store, snapshotStore, cache, device.GetOrCreateDeviceId());
        });

        // Application Handlers
        services.AddTransient<CreateObligationHandler>();
        services.AddTransient<DefineScheduleHandler>();
        services.AddTransient<RecordPaymentHandler>();
        services.AddTransient<GetFinancialSnapshotHandler>();
        services.AddTransient<GetPortfolioDashboardHandler>();
        services.AddTransient<GetDashboardSummaryHandler>();
        services.AddTransient<GetFinancialHealthHandler>();
        services.AddTransient<GetBalanceSheetHandler>();
        services.AddTransient<InstallRulePackHandler>();
        services.AddTransient<AssignRulePackToObligationHandler>();
        services.AddTransient<SimulateScenarioHandler>();
        services.AddTransient<CloseObligationHandler>();
        services.AddTransient<WaiveChargeHandler>();
        services.AddTransient<CreatePersonHandler>();
        services.AddTransient<RegisterInstitutionHandler>();
        services.AddTransient<GetInstalledRulePacksHandler>();
        services.AddTransient<GetRulePackAssignmentHandler>();
        services.AddTransient<GetObligationsListHandler>();
        services.AddTransient<GetPaymentsLedgerHandler>();
        services.AddTransient<ReversePaymentHandler>();
        services.AddTransient<PreviewPaymentAllocationHandler>();
        services.AddTransient<GetAuditTrailHandler>();
        services.AddTransient<GetChargeBreakdownReportHandler>();
        services.AddTransient<CreateAccountHandler>();
        services.AddTransient<ArchiveAccountHandler>();
        services.AddTransient<RecordTransferHandler>();
        services.AddTransient<GetAccountsListHandler>();
        services.AddTransient(sp => new GetCashLedgerHandler(
            sp.GetRequiredService<IEventStore>(), sp.GetRequiredService<ProjectionRunner>()));
        services.AddTransient<CreateCategoryHandler>();
        services.AddTransient<RenameCategoryHandler>();
        services.AddTransient<ArchiveCategoryHandler>();
        services.AddTransient<GetCategoriesListHandler>();
        services.AddTransient<DefineBudgetHandler>();
        services.AddTransient<AdjustBudgetHandler>();
        services.AddTransient<ArchiveBudgetHandler>();
        services.AddTransient(sp => new GetBudgetDashboardHandler(
            sp.GetRequiredService<IEventStore>(), sp.GetRequiredService<ProjectionRunner>()));
        services.AddTransient<CreateRecurringHandler>();
        services.AddTransient<ArchiveRecurringHandler>();
        services.AddTransient<PostRecurringNowHandler>();
        services.AddTransient<GetRecurringDashboardHandler>();
        services.AddTransient<CreateBankImportProfileHandler>();
        services.AddTransient<ModifyBankImportProfileHandler>();
        services.AddTransient<ArchiveBankImportProfileHandler>();
        services.AddTransient<GetBankImportProfilesListHandler>();
        services.AddTransient<PreviewBankImportHandler>();
        services.AddTransient<StartBankImportBatchHandler>();
        services.AddTransient(sp => new GetReconciliationCandidatesHandler(
            sp.GetRequiredService<IEventStore>(), sp.GetRequiredService<ProjectionRunner>()));
        services.AddTransient<ApplyImportedTransactionHandler>();
        services.AddTransient<ConfirmMatchImportedTransactionHandler>();
        services.AddTransient<IgnoreImportedTransactionHandler>();
        services.AddTransient<RevertImportedDecisionHandler>();
        services.AddTransient<CorrectImportedDecisionHandler>();
        services.AddTransient<UndoImportBatchHandler>();
        services.AddTransient<BulkApplyUnmatchedHandler>();
        services.AddTransient<RecordSplitExpenseHandler>();
        services.AddTransient<RecordSplitIncomeHandler>();
        services.AddTransient<ReverseSplitExpenseHandler>();
        services.AddTransient<ReverseSplitIncomeHandler>();
        services.AddTransient<UpdateEntityTagsHandler>();
        services.AddTransient<GetTagSuggestionsHandler>();
        services.AddTransient<GetEntitiesByTagHandler>();
        services.AddTransient<CreateAssetHandler>();
        services.AddTransient<ArchiveAssetHandler>();
        services.AddTransient<RecordAssetPriceHandler>();
        services.AddTransient<RecordFxRateHandler>();
        services.AddTransient<AdjustAssetQuantityHandler>();
        services.AddTransient<GetAssetsListHandler>();
        services.AddTransient(sp => new GetNetWorthReportHandler(
            sp.GetRequiredService<IEventStore>(),
            sp.GetRequiredService<GetObligationsListHandler>(),
            sp.GetRequiredService<ProjectionRunner>()));
        services.AddTransient<CreateInvestmentAccountHandler>();
        services.AddTransient<ArchiveInvestmentAccountHandler>();
        services.AddTransient<RecordInvestmentTransactionHandler>();
        services.AddTransient<ReverseInvestmentTransactionHandler>();
        services.AddTransient<SetCostBasisModeHandler>();
        services.AddTransient(sp => new GetInvestmentPortfolioDashboardHandler(
            sp.GetRequiredService<IEventStore>(), sp.GetRequiredService<ProjectionRunner>()));
        services.AddTransient<GetHoldingDetailHandler>();
        services.AddTransient<GetPerformanceReportHandler>();
        services.AddTransient<CreateTaxProfileHandler>();
        services.AddTransient<ModifyTaxProfileHandler>();
        services.AddTransient<ArchiveTaxProfileHandler>();
        services.AddTransient<DefineTaxRuleHandler>();
        services.AddTransient<ArchiveTaxRuleHandler>();
        services.AddTransient<ConfirmTaxClassificationHandler>();
        services.AddTransient<GetTaxProfilesHandler>();
        services.AddTransient<GetTaxRulesHandler>();
        services.AddTransient(sp => new GetTaxYearReportHandler(
            sp.GetRequiredService<IEventStore>(), sp.GetRequiredService<ProjectionRunner>()));
        services.AddTransient<CreateFinancialGoalHandler>();
        services.AddTransient<ModifyFinancialGoalHandler>();
        services.AddTransient<ArchiveFinancialGoalHandler>();
        services.AddTransient<RecordGoalContributionHandler>();
        services.AddTransient<ReverseGoalContributionHandler>();
        services.AddTransient<GetGoalsDashboardHandler>();
        services.AddTransient<DefineRetirementProfileHandler>();
        services.AddTransient<SetRetirementAssumptionsHandler>();
        services.AddTransient<ArchiveRetirementAssumptionsHandler>();
        services.AddTransient<GetRetirementPlanReportHandler>();
        services.AddTransient<GetSetupStateHandler>();
        services.AddTransient<CompleteInitialSetupHandler>();
        services.AddTransient<CreateDefaultAccountsHandler>();
        services.AddTransient<CreateDefaultCategoriesHandler>();
        services.AddTransient<SeedDemoDataHandler>();
        services.AddTransient<ClearDemoDataHandler>();
        services.AddTransient<RunDataQualityScanHandler>();
        services.AddTransient<GetDataQualityDashboardHandler>();
        services.AddTransient<GetDataQualityIssuesHandler>();
        services.AddTransient<AcknowledgeIssueHandler>();
        services.AddTransient<ResolveIssueHandler>();
        services.AddTransient<PreviewFixHandler>();
        services.AddTransient<ApplyFixHandler>(sp => new ApplyFixHandler(
            sp.GetRequiredService<IEventStore>(),
            sp.GetRequiredService<RevertImportedDecisionHandler>(),
            sp.GetRequiredService<ArchiveRecurringHandler>()));
        services.AddTransient<PruneSnapshotsHandler>();
        services.AddTransient<ClearProjectionCacheHandler>();
        services.AddTransient<RebuildSnapshotsHandler>();
        services.AddTransient<GetCurrencySettingsHandler>();
        services.AddTransient<SetReportingCurrencyHandler>();
        services.AddTransient<SetFxPolicyHandler>();
        services.AddTransient<ArchiveCurrencySettingsHandler>();
        services.AddSingleton<ReportingCurrencyService>();

        // Forecasting + Scenario handlers
        services.AddTransient<GetBaselineForecastHandler>(sp => new GetBaselineForecastHandler(
            sp.GetRequiredService<IEventStore>(), sp.GetRequiredService<ProjectionRunner>()));
        services.AddTransient<GetScenarioForecastHandler>(sp => new GetScenarioForecastHandler(
            sp.GetRequiredService<IEventStore>(), sp.GetRequiredService<ProjectionRunner>()));
        services.AddTransient<GetForecastDashboardHandler>();
        services.AddTransient<CreateForecastScenarioHandler>();
        services.AddTransient<ModifyForecastScenarioHandler>();
        services.AddTransient<ArchiveForecastScenarioHandler>();
        services.AddTransient<AddScenarioChangeHandler>();
        services.AddTransient<RemoveScenarioChangeHandler>();
        services.AddTransient<GetScenarioListHandler>();
        services.AddTransient<GetScenarioDetailHandler>();

        // Parties, Contracts, Billing handlers
        services.AddTransient<CreatePartyHandler>();
        services.AddTransient<ModifyPartyHandler>();
        services.AddTransient<ArchivePartyHandler>();
        services.AddTransient<GetPartiesListHandler>();
        services.AddTransient<CreateContractHandler>();
        services.AddTransient<ModifyContractHandler>();
        services.AddTransient<ArchiveContractHandler>();
        services.AddTransient<GetContractsListHandler>();
        services.AddTransient<GetContractDetailHandler>();
        services.AddTransient<IssueBillHandler>();
        services.AddTransient<IssueInvoiceHandler>();
        services.AddTransient<CancelBillHandler>();
        services.AddTransient<CancelInvoiceHandler>();
        services.AddTransient<DisputeBillHandler>();
        services.AddTransient<DisputeInvoiceHandler>();
        services.AddTransient<WriteOffBillHandler>();
        services.AddTransient<WriteOffInvoiceHandler>();
        services.AddTransient<AddBillAdjustmentHandler>();
        services.AddTransient<AddInvoiceAdjustmentHandler>();
        services.AddTransient<ReverseBillAdjustmentHandler>();
        services.AddTransient<ReverseInvoiceAdjustmentHandler>();
        services.AddTransient<RecordBillPaymentHandler>();
        services.AddTransient<RecordInvoicePaymentHandler>();
        services.AddTransient<ReverseBillPaymentHandler>();
        services.AddTransient<ReverseInvoicePaymentHandler>();
        services.AddTransient<UnapplyBillPaymentHandler>();
        services.AddTransient<UnapplyInvoicePaymentHandler>();
        services.AddTransient<GetBillingDashboardHandler>();
        services.AddTransient<GetAgingReportHandler>();
        services.AddTransient<PreviewContractBillingGenerationHandler>();
        services.AddTransient<GenerateContractBillsHandler>();
        services.AddTransient<GenerateContractInvoicesHandler>();
        services.AddTransient<ApplyImportedTransactionToBillPaymentHandler>();
        services.AddTransient<ApplyImportedTransactionToInvoicePaymentHandler>();

        // Notification handlers
        services.AddTransient<CreateNotificationRuleHandler>();
        services.AddTransient<ModifyNotificationRuleHandler>();
        services.AddTransient<ArchiveNotificationRuleHandler>();
        services.AddTransient<AcknowledgeNotificationHandler>();
        services.AddTransient<DismissNotificationHandler>();
        services.AddTransient<SnoozeNotificationHandler>();
        services.AddTransient<LinkNotificationActionHandler>();
        services.AddTransient<GetNotificationRulesHandler>();
        services.AddTransient<GetNotificationCenterHandler>();

        // Toast Notifications
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<ToastHostViewModel>();

        // File Dialog and Export Services
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IExportService, ExportService>();

        // Focus Request Service (for keyboard shortcut focus routing)
        services.AddSingleton<IFocusRequestService, FocusRequestService>();

        // Theme Service
        services.AddSingleton<IThemeService, ThemeService>();

        // App Reload Service
        services.AddSingleton<IAppReloadService, AppReloadService>();

        // Vault Backup Service
        services.AddSingleton<IVaultBackupService>(sp =>
        {
            var factory = sp.GetRequiredService<SqliteConnectionFactory>();
            var fileDialog = sp.GetRequiredService<IFileDialogService>();
            var toastService = sp.GetRequiredService<IToastService>();
            var reloadService = sp.GetRequiredService<IAppReloadService>();
            return new VaultBackupService(dbPath, factory, fileDialog, toastService, reloadService);
        });

        // Document Vault - Encrypted Blob Store
        services.AddSingleton<DebtManager.Domain.Documents.IDocumentBlobStore>(sp =>
        {
            var keys = sp.GetRequiredService<IKeyStore>();
            return new EncryptedFileDocumentBlobStore(keys);
        });

        // Document Vault handlers
        services.AddTransient<AddDocumentHandler>();
        services.AddTransient<UpdateDocumentMetadataHandler>();
        services.AddTransient<ArchiveDocumentHandler>();
        services.AddTransient<PurgeDocumentBlobHandler>();
        services.AddTransient<LinkDocumentHandler>();
        services.AddTransient<UnlinkDocumentHandler>();
        services.AddTransient<ExportDocumentHandler>();
        services.AddTransient<GetDocumentVaultDashboardHandler>();
        services.AddTransient<GetDocumentsListHandler>();
        services.AddTransient<GetDocumentsForEntityHandler>();

        // Import Rule Pack handlers
        services.AddTransient<CreateImportRulePackHandler>();
        services.AddTransient<ModifyImportRulePackHandler>();
        services.AddTransient<ArchiveImportRulePackHandler>();
        services.AddTransient<DefineImportRuleHandler>();
        services.AddTransient<ArchiveImportRuleHandler>();
        services.AddTransient<GetImportRulePacksListHandler>();
        services.AddTransient<GetImportRulePackDetailHandler>();
        services.AddTransient<PreviewRuleAgainstBatchHandler>();
        services.AddTransient<GetImportSuggestionsHandler>(sp => new GetImportSuggestionsHandler(
            sp.GetRequiredService<IEventStore>(), sp.GetRequiredService<ProjectionRunner>()));
        services.AddTransient<ApplySuggestionHandler>(sp => new ApplySuggestionHandler(
            sp.GetRequiredService<IEventStore>(),
            sp.GetRequiredService<ApplyImportedTransactionHandler>(),
            sp.GetRequiredService<ConfirmMatchImportedTransactionHandler>(),
            sp.GetRequiredService<IgnoreImportedTransactionHandler>()));
        services.AddTransient<RunAutoApplyForBatchHandler>(sp => new RunAutoApplyForBatchHandler(
            sp.GetRequiredService<IEventStore>(),
            sp.GetRequiredService<GetImportSuggestionsHandler>(),
            sp.GetRequiredService<ApplySuggestionHandler>()));

        // AI Advisor handlers
        services.AddTransient<GetAiDashboardHandler>();
        services.AddTransient<RunAiAnalysisHandler>(sp => new RunAiAnalysisHandler(
            sp.GetRequiredService<IEventStore>(), sp.GetRequiredService<ProjectionRunner>()));
        services.AddTransient<ApproveAiProposalHandler>();
        services.AddTransient<RejectAiProposalHandler>();
        services.AddTransient<UpdateAiSettingsHandler>();
        services.AddTransient<GetAiSettingsHandler>();

        // Income Source handlers
        services.AddTransient<DefineIncomeSourceHandler>();
        services.AddTransient<ArchiveIncomeSourceHandler>();
        services.AddTransient<GetIncomeSourcesHandler>();
        services.AddTransient<GetIncomeBySourceReportHandler>();

        // Report Engine handlers
        services.AddTransient<GetAvailableReportsHandler>();
        services.AddTransient<GenerateReportHandler>(sp => new GenerateReportHandler(
            sp.GetRequiredService<IEventStore>(), sp.GetRequiredService<ProjectionRunner>()));

        // Identity handlers
        services.AddSingleton<LocalAuthVault>(sp =>
        {
            var keys = sp.GetRequiredService<IKeyStore>();
            return new LocalAuthVault(keys);
        });
        services.AddSingleton<AuthorizationService>();
        services.AddTransient<CreateVaultUserHandler>();
        services.AddTransient<ModifyVaultUserHandler>();
        services.AddTransient<ArchiveVaultUserHandler>();
        services.AddTransient<SetUserPasswordHandler>();
        services.AddTransient<LoginHandler>();
        services.AddTransient<LogoutHandler>();
        services.AddTransient<WhoAmIHandler>();
        services.AddTransient<ListVaultUsersHandler>();
        services.AddTransient<GrantPermissionOverrideHandler>();
        services.AddTransient<RevokePermissionOverrideHandler>();

        // ViewModels
        services.AddTransient<ShellViewModel>(sp =>
        {
            var store = sp.GetRequiredService<IEventStore>();
            var ruleEngine = sp.GetRequiredService<IRuleEngine>();
            var device = sp.GetRequiredService<DeviceIdentityProvider>();
            var auditLogger = sp.GetRequiredService<ISecurityAuditLogger>();
            var createHandler = sp.GetRequiredService<CreateObligationHandler>();
            var paymentHandler = sp.GetRequiredService<RecordPaymentHandler>();
            var scheduleHandler = sp.GetRequiredService<DefineScheduleHandler>();
            var dashboardHandler = sp.GetRequiredService<GetPortfolioDashboardHandler>();
            var snapshotHandler = sp.GetRequiredService<GetFinancialSnapshotHandler>();
            var closeHandler = sp.GetRequiredService<CloseObligationHandler>();
            var syncEngine = sp.GetRequiredService<SyncEngine>();
            var config = sp.GetRequiredService<SecureConfiguration>();
            var toastService = sp.GetRequiredService<IToastService>();
            var toastHost = sp.GetRequiredService<ToastHostViewModel>();
            var exportService = sp.GetRequiredService<IExportService>();
            var focusService = sp.GetRequiredService<IFocusRequestService>();
            var themeService = sp.GetRequiredService<IThemeService>();
            var backupService = sp.GetRequiredService<IVaultBackupService>();
            var reloadService = sp.GetRequiredService<IAppReloadService>();
            var installPackHandler = sp.GetRequiredService<InstallRulePackHandler>();
            var assignPackHandler = sp.GetRequiredService<AssignRulePackToObligationHandler>();
            var getPacksHandler = sp.GetRequiredService<GetInstalledRulePacksHandler>();
            var getAssignmentHandler = sp.GetRequiredService<GetRulePackAssignmentHandler>();
            var getObligationsListHandler = sp.GetRequiredService<GetObligationsListHandler>();
            var getPaymentsLedgerHandler = sp.GetRequiredService<GetPaymentsLedgerHandler>();
            var reversePaymentHandler = sp.GetRequiredService<ReversePaymentHandler>();
            var previewPaymentHandler = sp.GetRequiredService<PreviewPaymentAllocationHandler>();
            var simulateScenarioHandler = sp.GetRequiredService<SimulateScenarioHandler>();
            var getAuditTrailHandler = sp.GetRequiredService<GetAuditTrailHandler>();
            var getChargeBreakdownHandler = sp.GetRequiredService<GetChargeBreakdownReportHandler>();
            var createAccountHandler = sp.GetRequiredService<CreateAccountHandler>();
            var archiveAccountHandler = sp.GetRequiredService<ArchiveAccountHandler>();
            var getAccountsListHandler = sp.GetRequiredService<GetAccountsListHandler>();
            var getCashLedgerHandler = sp.GetRequiredService<GetCashLedgerHandler>();
            var createCategoryHandler = sp.GetRequiredService<CreateCategoryHandler>();
            var renameCategoryHandler = sp.GetRequiredService<RenameCategoryHandler>();
            var archiveCategoryHandler = sp.GetRequiredService<ArchiveCategoryHandler>();
            var getCategoriesListHandler = sp.GetRequiredService<GetCategoriesListHandler>();
            var defineBudgetHandler = sp.GetRequiredService<DefineBudgetHandler>();
            var archiveBudgetHandler = sp.GetRequiredService<ArchiveBudgetHandler>();
            var getBudgetDashboardHandler = sp.GetRequiredService<GetBudgetDashboardHandler>();
            var createRecurringHandler = sp.GetRequiredService<CreateRecurringHandler>();
            var archiveRecurringHandler = sp.GetRequiredService<ArchiveRecurringHandler>();
            var postRecurringNowHandler = sp.GetRequiredService<PostRecurringNowHandler>();
            var getRecurringDashboardHandler = sp.GetRequiredService<GetRecurringDashboardHandler>();
            var createImportProfileHandler = sp.GetRequiredService<CreateBankImportProfileHandler>();
            var modifyImportProfileHandler = sp.GetRequiredService<ModifyBankImportProfileHandler>();
            var archiveImportProfileHandler = sp.GetRequiredService<ArchiveBankImportProfileHandler>();
            var getImportProfilesHandler = sp.GetRequiredService<GetBankImportProfilesListHandler>();
            var previewImportHandler = sp.GetRequiredService<PreviewBankImportHandler>();
            var startImportHandler = sp.GetRequiredService<StartBankImportBatchHandler>();
            var getReconciliationHandler = sp.GetRequiredService<GetReconciliationCandidatesHandler>();
            var applyImportedHandler = sp.GetRequiredService<ApplyImportedTransactionHandler>();
            var confirmMatchHandler = sp.GetRequiredService<ConfirmMatchImportedTransactionHandler>();
            var ignoreImportedHandler = sp.GetRequiredService<IgnoreImportedTransactionHandler>();
            var revertDecisionHandler = sp.GetRequiredService<RevertImportedDecisionHandler>();
            var correctDecisionHandler = sp.GetRequiredService<CorrectImportedDecisionHandler>();
            var undoBatchHandler = sp.GetRequiredService<UndoImportBatchHandler>();
            var bulkApplyHandler = sp.GetRequiredService<BulkApplyUnmatchedHandler>();
            var fileDialogService = sp.GetRequiredService<IFileDialogService>();
            var createAssetHandler = sp.GetRequiredService<CreateAssetHandler>();
            var archiveAssetHandler = sp.GetRequiredService<ArchiveAssetHandler>();
            var recordAssetPriceHandler = sp.GetRequiredService<RecordAssetPriceHandler>();
            var adjustAssetQuantityHandler = sp.GetRequiredService<AdjustAssetQuantityHandler>();
            var getAssetsListHandler = sp.GetRequiredService<GetAssetsListHandler>();
            var getNetWorthReportHandler = sp.GetRequiredService<GetNetWorthReportHandler>();
            var createInvestmentAccountHandler = sp.GetRequiredService<CreateInvestmentAccountHandler>();
            var archiveInvestmentAccountHandler = sp.GetRequiredService<ArchiveInvestmentAccountHandler>();
            var recordInvestmentTransactionHandler = sp.GetRequiredService<RecordInvestmentTransactionHandler>();
            var setCostBasisModeHandler = sp.GetRequiredService<SetCostBasisModeHandler>();
            var getInvestmentDashboardHandler = sp.GetRequiredService<GetInvestmentPortfolioDashboardHandler>();
            var getHoldingDetailHandler = sp.GetRequiredService<GetHoldingDetailHandler>();
            var createTaxProfileHandler = sp.GetRequiredService<CreateTaxProfileHandler>();
            var modifyTaxProfileHandler = sp.GetRequiredService<ModifyTaxProfileHandler>();
            var archiveTaxProfileHandler = sp.GetRequiredService<ArchiveTaxProfileHandler>();
            var defineTaxRuleHandler = sp.GetRequiredService<DefineTaxRuleHandler>();
            var archiveTaxRuleHandler = sp.GetRequiredService<ArchiveTaxRuleHandler>();
            var confirmTaxClassificationHandler = sp.GetRequiredService<ConfirmTaxClassificationHandler>();
            var getTaxProfilesHandler = sp.GetRequiredService<GetTaxProfilesHandler>();
            var getTaxRulesHandler = sp.GetRequiredService<GetTaxRulesHandler>();
            var getTaxYearReportHandler = sp.GetRequiredService<GetTaxYearReportHandler>();
            var createGoalHandler = sp.GetRequiredService<CreateFinancialGoalHandler>();
            var modifyGoalHandler = sp.GetRequiredService<ModifyFinancialGoalHandler>();
            var archiveGoalHandler = sp.GetRequiredService<ArchiveFinancialGoalHandler>();
            var recordContribHandler = sp.GetRequiredService<RecordGoalContributionHandler>();
            var reverseContribHandler = sp.GetRequiredService<ReverseGoalContributionHandler>();
            var goalsDashboardHandler = sp.GetRequiredService<GetGoalsDashboardHandler>();
            var defineRetirementProfileHandler = sp.GetRequiredService<DefineRetirementProfileHandler>();
            var setRetirementAssumptionsHandler = sp.GetRequiredService<SetRetirementAssumptionsHandler>();
            var archiveRetirementAssumptionsHandler = sp.GetRequiredService<ArchiveRetirementAssumptionsHandler>();
            var getRetirementPlanHandler = sp.GetRequiredService<GetRetirementPlanReportHandler>();

            // Get vault ID from config, or use device ID as fallback
            var vaultId = config.Get(ConfigKeys.SyncVaultId) ?? device.GetOrCreateDeviceId().ToString();

            var deviceId = device.GetOrCreateDeviceId();
            var actorUserId = Guid.NewGuid(); // In real app, would be from auth

            // Create Rule Pack Manager ViewModel
            var tagging = new TaggingMixin(
                sp.GetRequiredService<UpdateEntityTagsHandler>(),
                sp.GetRequiredService<GetTagSuggestionsHandler>(),
                sp.GetRequiredService<GetEntitiesByTagHandler>(),
                store,
                actorUserId,
                deviceId,
                toastService);

            var rulePackManagerVm = new RulePackManagerViewModel(
                store,
                installPackHandler,
                assignPackHandler,
                getPacksHandler,
                getAssignmentHandler,
                actorUserId,
                deviceId,
                toastService
            );

            // Create ShellViewModel first, then DashboardViewModel with navigation callback
            ShellViewModel? shell = null;
            DashboardViewModel? dashboardVm = null;
            var setupStateHandler = sp.GetRequiredService<GetSetupStateHandler>();
            var seedDemoHandler = sp.GetRequiredService<SeedDemoDataHandler>();
            var summaryHandler = sp.GetRequiredService<GetDashboardSummaryHandler>();
            var healthHandler = sp.GetRequiredService<GetFinancialHealthHandler>();
            dashboardVm = new DashboardViewModel(
                dashboardHandler,
                onOpenObligation: obligationId => shell?.NavigateToObligationDetail(obligationId),
                onCreateObligation: () => shell?.CreateObligationCommand.Execute(null),
                setupStateHandler: setupStateHandler,
                onOpenSetupWizard: () => shell?.StartInitialSetup(),
                onNavigateToAccounts: () => shell?.NavigateToAccountsCommand.Execute(null),
                onNavigateToImport: () => shell?.NavigateToImportCommand.Execute(null),
                onSeedDemo: async () =>
                {
                    await seedDemoHandler.HandleAsync("EGP", actorUserId, deviceId, CancellationToken.None);
                    await dashboardVm!.RefreshAsync();
                },
                onNavigateToCashLedger: () => shell?.NavigateToCashLedgerCommand.Execute(null),
                onNavigateToInvestments: () => shell?.NavigateToPortfolioInvestmentsCommand.Execute(null),
                onNavigateToGoals: () => shell?.NavigateToGoalsCommand.Execute(null),
                onNavigateToObligations: () => shell?.NavigateToObligationsCommand.Execute(null),
                summaryHandler: summaryHandler,
                onNavigateToForecast: () => shell?.NavigateToForecastCommand.Execute(null),
                onNavigateToAiAdvisor: () => shell?.NavigateToAiAdvisorCommand.Execute(null),
                onNavigateToReports: () => shell?.NavigateToReportsCommand.Execute(null),
                onNavigateToDataQuality: () => shell?.NavigateToDataQualityCommand.Execute(null),
                healthHandler: healthHandler
            );

            // Create ObligationsListViewModel with full callbacks
            // Note: The shell callbacks will be wired up after shell creation
            ObligationsListViewModel? obligationsListVm = null;
            obligationsListVm = new ObligationsListViewModel(
                listHandler: getObligationsListHandler,
                closeHandler: closeHandler,
                actorUserId: actorUserId,
                deviceId: deviceId,
                onCreateObligation: () => shell?.CreateObligationCommand.Execute(null),
                onOpenDetail: obligationId => shell?.NavigateToObligationDetail(obligationId),
                onRecordPayment: obligationId => shell?.ShowRecordPaymentForObligationFromList(obligationId),
                onDefineSchedule: obligationId => shell?.ShowDefineScheduleForObligationFromList(obligationId),
                toastService: toastService,
                exportService: exportService,
                tagging: tagging
            );

            // Create PaymentsListViewModel with full callbacks
            var paymentsListVm = new PaymentsListViewModel(
                ledgerHandler: getPaymentsLedgerHandler,
                reverseHandler: reversePaymentHandler,
                obligationsHandler: getObligationsListHandler,
                actorUserId: actorUserId,
                deviceId: deviceId,
                onRecordPayment: () => shell?.RecordPaymentCommand.Execute(null),
                toastService: toastService,
                exportService: exportService
            );

            // Create ScenarioSimulationViewModel
            var simulationVm = new ScenarioSimulationViewModel(
                simulateHandler: simulateScenarioHandler,
                obligationsHandler: getObligationsListHandler,
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService,
                defaultCurrencyCode: "EGP"
            );

            // Create AuditTrailViewModel
            var auditVm = new AuditTrailViewModel(
                auditHandler: getAuditTrailHandler,
                obligationsHandler: getObligationsListHandler,
                toastService: toastService,
                exportService: exportService
            );

            // Create ChargeBreakdownViewModel
            var chargeBreakdownVm = new ChargeBreakdownViewModel(
                reportHandler: getChargeBreakdownHandler,
                obligationsHandler: getObligationsListHandler,
                exportService: exportService,
                toastService: toastService,
                onCreateObligation: () => shell?.CreateObligationCommand.Execute(null)
            );

            // Create AccountsViewModel
            var accountsVm = new AccountsViewModel(
                listHandler: getAccountsListHandler,
                createHandler: createAccountHandler,
                archiveHandler: archiveAccountHandler,
                updateTagsHandler: sp.GetRequiredService<UpdateEntityTagsHandler>(),
                suggestionsHandler: sp.GetRequiredService<GetTagSuggestionsHandler>(),
                eventStore: sp.GetRequiredService<IEventStore>(),
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService
            );

            // Create CashLedgerViewModel
            var cashLedgerVm = new CashLedgerViewModel(
                ledgerHandler: getCashLedgerHandler,
                accountsHandler: getAccountsListHandler,
                toastService: toastService,
                exportService: exportService,
                categoriesHandler: getCategoriesListHandler,
                splitExpenseHandler: sp.GetRequiredService<RecordSplitExpenseHandler>(),
                splitIncomeHandler: sp.GetRequiredService<RecordSplitIncomeHandler>(),
                actorUserId: actorUserId,
                deviceId: deviceId,
                incomeSourcesHandler: sp.GetRequiredService<GetIncomeSourcesHandler>(),
                onManageIncomeSources: () => shell?.NavigateToIncomeSourcesCommand.Execute(null)
            );

            // Create CategoriesViewModel
            var categoriesVm = new CategoriesViewModel(
                listHandler: getCategoriesListHandler,
                createHandler: createCategoryHandler,
                renameHandler: renameCategoryHandler,
                archiveHandler: archiveCategoryHandler,
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService
            );

            // Create BudgetsViewModel
            var budgetsVm = new BudgetsViewModel(
                dashboardHandler: getBudgetDashboardHandler,
                defineHandler: defineBudgetHandler,
                archiveHandler: archiveBudgetHandler,
                categoriesHandler: getCategoriesListHandler,
                accountsHandler: getAccountsListHandler,
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService
            );

            // Create RecurringViewModel
            var recurringVm = new RecurringViewModel(
                dashboardHandler: getRecurringDashboardHandler,
                createHandler: createRecurringHandler,
                archiveHandler: archiveRecurringHandler,
                postHandler: postRecurringNowHandler,
                accountsHandler: getAccountsListHandler,
                categoriesHandler: getCategoriesListHandler,
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService,
                tagging: tagging
            );

            // Create ImportViewModel
            var importVm = new ImportViewModel(
                createProfileHandler: createImportProfileHandler,
                modifyProfileHandler: modifyImportProfileHandler,
                archiveProfileHandler: archiveImportProfileHandler,
                getProfilesHandler: getImportProfilesHandler,
                previewHandler: previewImportHandler,
                importHandler: startImportHandler,
                reconcileHandler: getReconciliationHandler,
                applyHandler: applyImportedHandler,
                matchHandler: confirmMatchHandler,
                ignoreHandler: ignoreImportedHandler,
                revertHandler: revertDecisionHandler,
                correctHandler: correctDecisionHandler,
                undoBatchHandler: undoBatchHandler,
                bulkApplyHandler: bulkApplyHandler,
                accountsHandler: getAccountsListHandler,
                suggestionsHandler: sp.GetRequiredService<GetImportSuggestionsHandler>(),
                applySuggestionHandler: sp.GetRequiredService<ApplySuggestionHandler>(),
                autoApplyHandler: sp.GetRequiredService<RunAutoApplyForBatchHandler>(),
                splitExpenseHandler: sp.GetRequiredService<RecordSplitExpenseHandler>(),
                categoriesHandler: getCategoriesListHandler,
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService,
                fileDialogService: fileDialogService
            );

            // Create AssetsViewModel
            var assetsVm = new AssetsViewModel(
                listHandler: getAssetsListHandler,
                createHandler: createAssetHandler,
                archiveHandler: archiveAssetHandler,
                priceHandler: recordAssetPriceHandler,
                adjustHandler: adjustAssetQuantityHandler,
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                tagging: tagging
            );

            // Create NetWorthViewModel
            var balanceSheetHandler = sp.GetRequiredService<GetBalanceSheetHandler>();
            var netWorthVm = new NetWorthViewModel(
                reportHandler: getNetWorthReportHandler,
                toastService: toastService,
                exportService: exportService,
                balanceSheetHandler: balanceSheetHandler
            );

            // Create InvestmentAccountsViewModel
            var investmentAccountsVm = new InvestmentAccountsViewModel(
                createHandler: createInvestmentAccountHandler,
                archiveHandler: archiveInvestmentAccountHandler,
                modeHandler: setCostBasisModeHandler,
                dashboardHandler: getInvestmentDashboardHandler,
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                tagging: tagging
            );

            // Create PortfolioViewModel (investments)
            var portfolioInvestmentsVm = new PortfolioViewModel(
                dashboardHandler: getInvestmentDashboardHandler,
                txnHandler: recordInvestmentTransactionHandler,
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService
            );

            // Create HoldingDetailViewModel
            var holdingDetailVm = new HoldingDetailViewModel(
                detailHandler: getHoldingDetailHandler,
                toastService: toastService,
                exportService: exportService
            );

            // Create TaxesViewModel
            var taxesVm = new TaxesViewModel(
                createProfileHandler: createTaxProfileHandler,
                modifyProfileHandler: modifyTaxProfileHandler,
                archiveProfileHandler: archiveTaxProfileHandler,
                defineRuleHandler: defineTaxRuleHandler,
                archiveRuleHandler: archiveTaxRuleHandler,
                confirmHandler: confirmTaxClassificationHandler,
                getProfilesHandler: getTaxProfilesHandler,
                getRulesHandler: getTaxRulesHandler,
                reportHandler: getTaxYearReportHandler,
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService
            );

            // Create GoalsViewModel
            var goalsVm = new GoalsViewModel(
                createHandler: createGoalHandler,
                modifyHandler: modifyGoalHandler,
                archiveHandler: archiveGoalHandler,
                contribHandler: recordContribHandler,
                reverseHandler: reverseContribHandler,
                dashboardHandler: goalsDashboardHandler,
                accountsHandler: getAccountsListHandler,
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService,
                tagging: tagging
            );

            // Create RetirementViewModel
            var retirementVm = new RetirementViewModel(
                profileHandler: defineRetirementProfileHandler,
                assumptionsHandler: setRetirementAssumptionsHandler,
                archiveAssumptionsHandler: archiveRetirementAssumptionsHandler,
                reportHandler: getRetirementPlanHandler,
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService
            );

            // Create DataQualityViewModel
            var dataQualityVm = new DataQualityViewModel(
                scanHandler: sp.GetRequiredService<RunDataQualityScanHandler>(),
                dashboardHandler: sp.GetRequiredService<GetDataQualityDashboardHandler>(),
                issuesHandler: sp.GetRequiredService<GetDataQualityIssuesHandler>(),
                acknowledgeHandler: sp.GetRequiredService<AcknowledgeIssueHandler>(),
                resolveHandler: sp.GetRequiredService<ResolveIssueHandler>(),
                previewHandler: sp.GetRequiredService<PreviewFixHandler>(),
                applyHandler: sp.GetRequiredService<ApplyFixHandler>(),
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService
            );

            // Create CurrencySettingsViewModel
            var currencySettingsVm = new CurrencySettingsViewModel(
                getHandler: sp.GetRequiredService<GetCurrencySettingsHandler>(),
                setCurrencyHandler: sp.GetRequiredService<SetReportingCurrencyHandler>(),
                setPolicyHandler: sp.GetRequiredService<SetFxPolicyHandler>(),
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService
            );

            // Create ForecastViewModel
            var forecastVm = new ForecastViewModel(
                baselineHandler: sp.GetRequiredService<GetBaselineForecastHandler>(),
                dashboardHandler: sp.GetRequiredService<GetForecastDashboardHandler>(),
                toastService: toastService,
                exportService: exportService
            );

            // Create ScenariosViewModel
            var scenariosVm = new ScenariosViewModel(
                createHandler: sp.GetRequiredService<CreateForecastScenarioHandler>(),
                archiveHandler: sp.GetRequiredService<ArchiveForecastScenarioHandler>(),
                addChangeHandler: sp.GetRequiredService<AddScenarioChangeHandler>(),
                removeChangeHandler: sp.GetRequiredService<RemoveScenarioChangeHandler>(),
                listHandler: sp.GetRequiredService<GetScenarioListHandler>(),
                detailHandler: sp.GetRequiredService<GetScenarioDetailHandler>(),
                forecastHandler: sp.GetRequiredService<GetScenarioForecastHandler>(),
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService
            );

            // Create PartiesViewModel
            var partiesVm = new PartiesViewModel(
                createHandler: sp.GetRequiredService<CreatePartyHandler>(),
                modifyHandler: sp.GetRequiredService<ModifyPartyHandler>(),
                archiveHandler: sp.GetRequiredService<ArchivePartyHandler>(),
                listHandler: sp.GetRequiredService<GetPartiesListHandler>(),
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService,
                tagging: tagging
            );

            // Create ContractsViewModel
            var contractsVm = new ContractsViewModel(
                createHandler: sp.GetRequiredService<CreateContractHandler>(),
                modifyHandler: sp.GetRequiredService<ModifyContractHandler>(),
                archiveHandler: sp.GetRequiredService<ArchiveContractHandler>(),
                listHandler: sp.GetRequiredService<GetContractsListHandler>(),
                detailHandler: sp.GetRequiredService<GetContractDetailHandler>(),
                previewGenHandler: sp.GetRequiredService<PreviewContractBillingGenerationHandler>(),
                genBillsHandler: sp.GetRequiredService<GenerateContractBillsHandler>(),
                genInvoicesHandler: sp.GetRequiredService<GenerateContractInvoicesHandler>(),
                partiesHandler: sp.GetRequiredService<GetPartiesListHandler>(),
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService,
                tagging: tagging
            );

            // Create BillsViewModel
            var billsVm = new BillsViewModel(
                issueHandler: sp.GetRequiredService<IssueBillHandler>(),
                cancelHandler: sp.GetRequiredService<CancelBillHandler>(),
                payHandler: sp.GetRequiredService<RecordBillPaymentHandler>(),
                reversePayHandler: sp.GetRequiredService<ReverseBillPaymentHandler>(),
                unapplyHandler: sp.GetRequiredService<UnapplyBillPaymentHandler>(),
                adjustHandler: sp.GetRequiredService<AddBillAdjustmentHandler>(),
                disputeHandler: sp.GetRequiredService<DisputeBillHandler>(),
                writeOffHandler: sp.GetRequiredService<WriteOffBillHandler>(),
                dashboardHandler: sp.GetRequiredService<GetBillingDashboardHandler>(),
                agingHandler: sp.GetRequiredService<GetAgingReportHandler>(),
                partiesHandler: sp.GetRequiredService<GetPartiesListHandler>(),
                accountsHandler: getAccountsListHandler,
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService,
                tagging: tagging
            );

            // Create InvoicesViewModel
            var invoicesVm = new InvoicesViewModel(
                issueHandler: sp.GetRequiredService<IssueInvoiceHandler>(),
                cancelHandler: sp.GetRequiredService<CancelInvoiceHandler>(),
                payHandler: sp.GetRequiredService<RecordInvoicePaymentHandler>(),
                reversePayHandler: sp.GetRequiredService<ReverseInvoicePaymentHandler>(),
                unapplyHandler: sp.GetRequiredService<UnapplyInvoicePaymentHandler>(),
                adjustHandler: sp.GetRequiredService<AddInvoiceAdjustmentHandler>(),
                disputeHandler: sp.GetRequiredService<DisputeInvoiceHandler>(),
                writeOffHandler: sp.GetRequiredService<WriteOffInvoiceHandler>(),
                dashboardHandler: sp.GetRequiredService<GetBillingDashboardHandler>(),
                agingHandler: sp.GetRequiredService<GetAgingReportHandler>(),
                partiesHandler: sp.GetRequiredService<GetPartiesListHandler>(),
                accountsHandler: getAccountsListHandler,
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService,
                tagging: tagging
            );

            // Create NotificationsViewModel
            var notificationsVm = new NotificationsViewModel(
                centerHandler: sp.GetRequiredService<GetNotificationCenterHandler>(),
                rulesHandler: sp.GetRequiredService<GetNotificationRulesHandler>(),
                createRuleHandler: sp.GetRequiredService<CreateNotificationRuleHandler>(),
                modifyRuleHandler: sp.GetRequiredService<ModifyNotificationRuleHandler>(),
                archiveRuleHandler: sp.GetRequiredService<ArchiveNotificationRuleHandler>(),
                acknowledgeHandler: sp.GetRequiredService<AcknowledgeNotificationHandler>(),
                dismissHandler: sp.GetRequiredService<DismissNotificationHandler>(),
                snoozeHandler: sp.GetRequiredService<SnoozeNotificationHandler>(),
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService
            );

            // Create DocumentVaultViewModel
            var documentVaultVm = new DocumentVaultViewModel(
                addHandler: sp.GetRequiredService<AddDocumentHandler>(),
                updateHandler: sp.GetRequiredService<UpdateDocumentMetadataHandler>(),
                archiveHandler: sp.GetRequiredService<ArchiveDocumentHandler>(),
                purgeHandler: sp.GetRequiredService<PurgeDocumentBlobHandler>(),
                exportHandler: sp.GetRequiredService<ExportDocumentHandler>(),
                linkHandler: sp.GetRequiredService<LinkDocumentHandler>(),
                unlinkHandler: sp.GetRequiredService<UnlinkDocumentHandler>(),
                dashboardHandler: sp.GetRequiredService<GetDocumentVaultDashboardHandler>(),
                listHandler: sp.GetRequiredService<GetDocumentsListHandler>(),
                actorUserId: actorUserId,
                deviceId: deviceId,
                fileDialogService: fileDialogService,
                toastService: toastService,
                exportService: exportService
            );

            // Create ImportRulesViewModel
            var importRulesVm = new ImportRulesViewModel(
                createPackHandler: sp.GetRequiredService<CreateImportRulePackHandler>(),
                modifyPackHandler: sp.GetRequiredService<ModifyImportRulePackHandler>(),
                archivePackHandler: sp.GetRequiredService<ArchiveImportRulePackHandler>(),
                defineRuleHandler: sp.GetRequiredService<DefineImportRuleHandler>(),
                archiveRuleHandler: sp.GetRequiredService<ArchiveImportRuleHandler>(),
                getPacksHandler: sp.GetRequiredService<GetImportRulePacksListHandler>(),
                getDetailHandler: sp.GetRequiredService<GetImportRulePackDetailHandler>(),
                previewHandler: sp.GetRequiredService<PreviewRuleAgainstBatchHandler>(),
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService
            );

            // Create AiAdvisorViewModel
            var aiAdvisorVm = new AiAdvisorViewModel(
                dashboardHandler: sp.GetRequiredService<GetAiDashboardHandler>(),
                analysisHandler: sp.GetRequiredService<RunAiAnalysisHandler>(),
                approveHandler: sp.GetRequiredService<ApproveAiProposalHandler>(),
                rejectHandler: sp.GetRequiredService<RejectAiProposalHandler>(),
                settingsHandler: sp.GetRequiredService<UpdateAiSettingsHandler>(),
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService
            );

            // Create IncomeSourcesViewModel
            var incomeSourcesVm = new IncomeSourcesViewModel(
                defineHandler: sp.GetRequiredService<DefineIncomeSourceHandler>(),
                archiveHandler: sp.GetRequiredService<ArchiveIncomeSourceHandler>(),
                listHandler: sp.GetRequiredService<GetIncomeSourcesHandler>(),
                reportHandler: sp.GetRequiredService<GetIncomeBySourceReportHandler>(),
                actorUserId: actorUserId,
                deviceId: deviceId,
                toastService: toastService,
                exportService: exportService
            );

            // Create ReportsViewModel
            var reportsVm = new ReportsViewModel(
                availableHandler: sp.GetRequiredService<GetAvailableReportsHandler>(),
                generateHandler: sp.GetRequiredService<GenerateReportHandler>(),
                exportService: exportService,
                toastService: toastService
            );

            shell = new ShellViewModel(
                store,
                ruleEngine,
                dashboardVm,
                actorUserId: actorUserId,
                deviceId: deviceId,
                auditLogger: auditLogger,
                createObligationHandler: createHandler,
                recordPaymentHandler: paymentHandler,
                defineScheduleHandler: scheduleHandler,
                snapshotHandler: snapshotHandler,
                closeObligationHandler: closeHandler,
                previewPaymentHandler: previewPaymentHandler,
                ledgerHandler: getPaymentsLedgerHandler,
                reversePaymentHandler: reversePaymentHandler,
                syncEngine: syncEngine,
                vaultId: vaultId,
                secureConfiguration: config,
                toastService: toastService,
                toastHost: toastHost,
                rulePackManagerVm: rulePackManagerVm,
                obligationsListVm: obligationsListVm,
                paymentsListVm: paymentsListVm,
                simulationVm: simulationVm,
                auditVm: auditVm,
                chargeBreakdownVm: chargeBreakdownVm,
                focusService: focusService,
                themeService: themeService,
                backupService: backupService,
                reloadService: reloadService,
                accountsVm: accountsVm,
                cashLedgerVm: cashLedgerVm,
                categoriesVm: categoriesVm,
                budgetsVm: budgetsVm,
                recurringVm: recurringVm,
                importVm: importVm,
                assetsVm: assetsVm,
                netWorthVm: netWorthVm,
                investmentAccountsVm: investmentAccountsVm,
                portfolioInvestmentsVm: portfolioInvestmentsVm,
                holdingDetailVm: holdingDetailVm,
                taxesVm: taxesVm,
                goalsVm: goalsVm,
                retirementVm: retirementVm,
                dataQualityVm: dataQualityVm,
                currencySettingsVm: currencySettingsVm,
                forecastVm: forecastVm,
                scenariosVm: scenariosVm,
                partiesVm: partiesVm,
                contractsVm: contractsVm,
                billsVm: billsVm,
                invoicesVm: invoicesVm,
                notificationsVm: notificationsVm,
                documentVaultVm: documentVaultVm,
                importRulesVm: importRulesVm,
                aiAdvisorVm: aiAdvisorVm,
                incomeSourcesVm: incomeSourcesVm,
                reportsVm: reportsVm,
                getSetupStateHandler: sp.GetRequiredService<GetSetupStateHandler>(),
                completeSetupHandler: sp.GetRequiredService<CompleteInitialSetupHandler>(),
                createDefaultAccountsHandler: sp.GetRequiredService<CreateDefaultAccountsHandler>(),
                createDefaultCategoriesHandler: sp.GetRequiredService<CreateDefaultCategoriesHandler>(),
                seedDemoHandler: sp.GetRequiredService<SeedDemoDataHandler>());

            return shell;
        });
        services.AddTransient<CreateObligationViewModel>(sp =>
        {
            var handler = sp.GetRequiredService<CreateObligationHandler>();
            var device = sp.GetRequiredService<DeviceIdentityProvider>();
            return new CreateObligationViewModel(handler, Guid.NewGuid(), device.GetOrCreateDeviceId());
        });
        services.AddTransient<RecordPaymentViewModel>(sp =>
        {
            var handler = sp.GetRequiredService<RecordPaymentHandler>();
            var store = sp.GetRequiredService<IEventStore>();
            var device = sp.GetRequiredService<DeviceIdentityProvider>();
            var previewHandler = sp.GetRequiredService<PreviewPaymentAllocationHandler>();
            return new RecordPaymentViewModel(
                handler,
                store,
                Guid.NewGuid(),
                device.GetOrCreateDeviceId(),
                previewHandler: previewHandler);
        });

        return services.BuildServiceProvider();
    }
}