using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Scheduling;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Services.Serialization;

public static class EnvelopeDeserializer
{
    public static IEnumerable<IDomainEvent> ToDomainEvents(IEnumerable<EventEnvelope> envelopes)
    {
        foreach (var e in envelopes)
        {
            IDomainEvent? ev = null;
            var opt = DomainJson.Options;

            if (e.EventType == nameof(ObligationCreated))
                ev = JsonSerializer.Deserialize<ObligationCreated>(e.PayloadJson, opt);

            else if (e.EventType == nameof(PaymentMade))
            {
                // v2+: payload is wrapped like:
                // { "paymentEventId": "...", "payment": { ...PaymentMade... } }
                // v1: payload is directly PaymentMade
                if (e.PayloadSchemaVersion >= 2)
                {
                    using var doc = JsonDocument.Parse(e.PayloadJson);

                    if (doc.RootElement.TryGetProperty("payment", out var paymentEl) ||
                        doc.RootElement.TryGetProperty("Payment", out paymentEl))
                    {
                        ev = JsonSerializer.Deserialize<PaymentMade>(paymentEl.GetRawText(), opt);
                    }
                    else
                    {
                        // fallback: if schema says wrapped but payload isn't, try direct
                        ev = JsonSerializer.Deserialize<PaymentMade>(e.PayloadJson, opt);
                    }
                }
                else
                {
                    ev = JsonSerializer.Deserialize<PaymentMade>(e.PayloadJson, opt);
                }

                // extra safety fallback for any historical weirdness
                if (ev is null && e.PayloadJson.Contains("\"payment\":", StringComparison.OrdinalIgnoreCase))
                {
                    using var doc = JsonDocument.Parse(e.PayloadJson);
                    if (doc.RootElement.TryGetProperty("payment", out var paymentEl) ||
                        doc.RootElement.TryGetProperty("Payment", out paymentEl))
                    {
                        ev = JsonSerializer.Deserialize<PaymentMade>(paymentEl.GetRawText(), opt);
                    }
                }
            }

            else if (e.EventType == nameof(PaymentAllocated))
                ev = JsonSerializer.Deserialize<PaymentAllocated>(e.PayloadJson, opt);

            else if (e.EventType == nameof(PaymentReversed))
                ev = JsonSerializer.Deserialize<PaymentReversed>(e.PayloadJson, opt);

            else if (e.EventType == nameof(PaymentAllocationReversed))
                ev = JsonSerializer.Deserialize<PaymentAllocationReversed>(e.PayloadJson, opt);

            else if (e.EventType == nameof(ChargeAllocated))
                ev = JsonSerializer.Deserialize<ChargeAllocated>(e.PayloadJson, opt);

            else if (e.EventType == nameof(PaymentUnapplied))
                ev = JsonSerializer.Deserialize<PaymentUnapplied>(e.PayloadJson, opt);

            else if (e.EventType == nameof(RulePackAssignedToObligation))
                ev = JsonSerializer.Deserialize<RulePackAssignedToObligation>(e.PayloadJson, opt);

            else if (e.EventType == "ScheduleDefined")
            {
                // Schedule events are not part of IDomainEvent stream replay (projection doesn’t apply them directly),
                // so we don't yield them here.
                ev = null;
            }

            else if (e.EventType == nameof(IncomeRecorded))
                ev = JsonSerializer.Deserialize<IncomeRecorded>(e.PayloadJson, opt);

            else if (e.EventType == nameof(ExpenseRecorded))
                ev = JsonSerializer.Deserialize<ExpenseRecorded>(e.PayloadJson, opt);

            else if (e.EventType == nameof(SplitExpenseRecorded))
                ev = JsonSerializer.Deserialize<SplitExpenseRecorded>(e.PayloadJson, opt);

            else if (e.EventType == nameof(SplitIncomeRecorded))
                ev = JsonSerializer.Deserialize<SplitIncomeRecorded>(e.PayloadJson, opt);

            else if (e.EventType == nameof(SplitExpenseReversed))
                ev = JsonSerializer.Deserialize<SplitExpenseReversed>(e.PayloadJson, opt);

            else if (e.EventType == nameof(SplitIncomeReversed))
                ev = JsonSerializer.Deserialize<SplitIncomeReversed>(e.PayloadJson, opt);

            else if (e.EventType == nameof(IncomeReceived))
                ev = JsonSerializer.Deserialize<IncomeReceived>(e.PayloadJson, opt);

            else if (e.EventType == nameof(AccountCreated))
                ev = JsonSerializer.Deserialize<AccountCreated>(e.PayloadJson, opt);

            else if (e.EventType == nameof(AccountArchived))
                ev = JsonSerializer.Deserialize<AccountArchived>(e.PayloadJson, opt);

            else if (e.EventType == nameof(TransferRecorded))
                ev = JsonSerializer.Deserialize<TransferRecorded>(e.PayloadJson, opt);

            else if (e.EventType == nameof(CategoryCreated))
                ev = JsonSerializer.Deserialize<CategoryCreated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(CategoryRenamed))
                ev = JsonSerializer.Deserialize<CategoryRenamed>(e.PayloadJson, opt);
            else if (e.EventType == nameof(CategoryArchived))
                ev = JsonSerializer.Deserialize<CategoryArchived>(e.PayloadJson, opt);

            else if (e.EventType == nameof(BudgetDefined))
                ev = JsonSerializer.Deserialize<BudgetDefined>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BudgetAdjusted))
                ev = JsonSerializer.Deserialize<BudgetAdjusted>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BudgetArchived))
                ev = JsonSerializer.Deserialize<BudgetArchived>(e.PayloadJson, opt);

            else if (e.EventType == nameof(RecurringTransactionCreated))
                ev = JsonSerializer.Deserialize<RecurringTransactionCreated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(RecurringTransactionModified))
                ev = JsonSerializer.Deserialize<RecurringTransactionModified>(e.PayloadJson, opt);
            else if (e.EventType == nameof(RecurringTransactionArchived))
                ev = JsonSerializer.Deserialize<RecurringTransactionArchived>(e.PayloadJson, opt);
            else if (e.EventType == nameof(RecurringTransactionPosted))
                ev = JsonSerializer.Deserialize<RecurringTransactionPosted>(e.PayloadJson, opt);

            else if (e.EventType == nameof(BankImportProfileCreated))
                ev = JsonSerializer.Deserialize<BankImportProfileCreated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BankImportProfileModified))
                ev = JsonSerializer.Deserialize<BankImportProfileModified>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BankImportProfileArchived))
                ev = JsonSerializer.Deserialize<BankImportProfileArchived>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BankImportBatchStarted))
                ev = JsonSerializer.Deserialize<BankImportBatchStarted>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BankTransactionImported))
                ev = JsonSerializer.Deserialize<BankTransactionImported>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BankImportBatchCompleted))
                ev = JsonSerializer.Deserialize<BankImportBatchCompleted>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BankTransactionMatched))
                ev = JsonSerializer.Deserialize<BankTransactionMatched>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BankTransactionIgnored))
                ev = JsonSerializer.Deserialize<BankTransactionIgnored>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BankTransactionApplied))
                ev = JsonSerializer.Deserialize<BankTransactionApplied>(e.PayloadJson, opt);

            else if (e.EventType == nameof(BankTransactionDecisionReverted))
                ev = JsonSerializer.Deserialize<BankTransactionDecisionReverted>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BankTransactionDecisionCorrected))
                ev = JsonSerializer.Deserialize<BankTransactionDecisionCorrected>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BankImportBatchUndoRequested))
                ev = JsonSerializer.Deserialize<BankImportBatchUndoRequested>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BankImportBatchUndoCompleted))
                ev = JsonSerializer.Deserialize<BankImportBatchUndoCompleted>(e.PayloadJson, opt);

            else if (e.EventType == nameof(IncomeReversed))
                ev = JsonSerializer.Deserialize<IncomeReversed>(e.PayloadJson, opt);
            else if (e.EventType == nameof(ExpenseReversed))
                ev = JsonSerializer.Deserialize<ExpenseReversed>(e.PayloadJson, opt);
            else if (e.EventType == nameof(TransferReversed))
                ev = JsonSerializer.Deserialize<TransferReversed>(e.PayloadJson, opt);

            else if (e.EventType == nameof(AssetCreated))
                ev = JsonSerializer.Deserialize<AssetCreated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(AssetUpdatedMetadata))
                ev = JsonSerializer.Deserialize<AssetUpdatedMetadata>(e.PayloadJson, opt);
            else if (e.EventType == nameof(AssetArchived))
                ev = JsonSerializer.Deserialize<AssetArchived>(e.PayloadJson, opt);
            else if (e.EventType == nameof(AssetPriceRecorded))
                ev = JsonSerializer.Deserialize<AssetPriceRecorded>(e.PayloadJson, opt);
            else if (e.EventType == nameof(FxRateRecorded))
                ev = JsonSerializer.Deserialize<FxRateRecorded>(e.PayloadJson, opt);
            else if (e.EventType == nameof(AssetQuantityAdjusted))
                ev = JsonSerializer.Deserialize<AssetQuantityAdjusted>(e.PayloadJson, opt);

            else if (e.EventType == nameof(InvestmentAccountCreated))
                ev = JsonSerializer.Deserialize<InvestmentAccountCreated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(InvestmentAccountArchived))
                ev = JsonSerializer.Deserialize<InvestmentAccountArchived>(e.PayloadJson, opt);
            else if (e.EventType == nameof(InvestmentTransactionRecorded))
                ev = JsonSerializer.Deserialize<InvestmentTransactionRecorded>(e.PayloadJson, opt);
            else if (e.EventType == nameof(InvestmentTransactionReversed))
                ev = JsonSerializer.Deserialize<InvestmentTransactionReversed>(e.PayloadJson, opt);
            else if (e.EventType == nameof(InvestmentCostBasisModeSet))
                ev = JsonSerializer.Deserialize<InvestmentCostBasisModeSet>(e.PayloadJson, opt);

            else if (e.EventType == nameof(TaxProfileCreated))
                ev = JsonSerializer.Deserialize<TaxProfileCreated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(TaxProfileModified))
                ev = JsonSerializer.Deserialize<TaxProfileModified>(e.PayloadJson, opt);
            else if (e.EventType == nameof(TaxProfileArchived))
                ev = JsonSerializer.Deserialize<TaxProfileArchived>(e.PayloadJson, opt);
            else if (e.EventType == nameof(TaxConfirmClassification))
                ev = JsonSerializer.Deserialize<TaxConfirmClassification>(e.PayloadJson, opt);
            else if (e.EventType == nameof(TaxRuleDefined))
                ev = JsonSerializer.Deserialize<TaxRuleDefined>(e.PayloadJson, opt);
            else if (e.EventType == nameof(TaxRuleArchived))
                ev = JsonSerializer.Deserialize<TaxRuleArchived>(e.PayloadJson, opt);

            else if (e.EventType == nameof(FinancialGoalCreated))
                ev = JsonSerializer.Deserialize<FinancialGoalCreated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(FinancialGoalModified))
                ev = JsonSerializer.Deserialize<FinancialGoalModified>(e.PayloadJson, opt);
            else if (e.EventType == nameof(FinancialGoalArchived))
                ev = JsonSerializer.Deserialize<FinancialGoalArchived>(e.PayloadJson, opt);
            else if (e.EventType == nameof(GoalContributionRecorded))
                ev = JsonSerializer.Deserialize<GoalContributionRecorded>(e.PayloadJson, opt);
            else if (e.EventType == nameof(GoalContributionReversed))
                ev = JsonSerializer.Deserialize<GoalContributionReversed>(e.PayloadJson, opt);

            else if (e.EventType == nameof(RetirementProfileDefined))
                ev = JsonSerializer.Deserialize<RetirementProfileDefined>(e.PayloadJson, opt);
            else if (e.EventType == nameof(RetirementAssumptionsSet))
                ev = JsonSerializer.Deserialize<RetirementAssumptionsSet>(e.PayloadJson, opt);
            else if (e.EventType == nameof(RetirementAssumptionsArchived))
                ev = JsonSerializer.Deserialize<RetirementAssumptionsArchived>(e.PayloadJson, opt);

            else if (e.EventType == nameof(InitialSetupCompleted))
                ev = JsonSerializer.Deserialize<InitialSetupCompleted>(e.PayloadJson, opt);
            else if (e.EventType == nameof(DemoDataSeeded))
                ev = JsonSerializer.Deserialize<DemoDataSeeded>(e.PayloadJson, opt);
            else if (e.EventType == nameof(DemoDataCleared))
                ev = JsonSerializer.Deserialize<DemoDataCleared>(e.PayloadJson, opt);
            else if (e.EventType == nameof(DefaultAccountsCreated))
                ev = JsonSerializer.Deserialize<DefaultAccountsCreated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(DefaultCategoriesCreated))
                ev = JsonSerializer.Deserialize<DefaultCategoriesCreated>(e.PayloadJson, opt);

            else if (e.EventType == nameof(DataQualityScanRecorded))
                ev = JsonSerializer.Deserialize<DataQualityScanRecorded>(e.PayloadJson, opt);
            else if (e.EventType == nameof(DataQualityIssueAcknowledged))
                ev = JsonSerializer.Deserialize<DataQualityIssueAcknowledged>(e.PayloadJson, opt);
            else if (e.EventType == nameof(DataQualityIssueResolved))
                ev = JsonSerializer.Deserialize<DataQualityIssueResolved>(e.PayloadJson, opt);
            else if (e.EventType == nameof(DataQualityAutoFixApplied))
                ev = JsonSerializer.Deserialize<DataQualityAutoFixApplied>(e.PayloadJson, opt);

            else if (e.EventType == nameof(ReportingCurrencySet))
                ev = JsonSerializer.Deserialize<ReportingCurrencySet>(e.PayloadJson, opt);
            else if (e.EventType == nameof(FxPolicySet))
                ev = JsonSerializer.Deserialize<FxPolicySet>(e.PayloadJson, opt);
            else if (e.EventType == nameof(CurrencySettingsArchived))
                ev = JsonSerializer.Deserialize<CurrencySettingsArchived>(e.PayloadJson, opt);

            else if (e.EventType == nameof(ForecastScenarioCreated))
                ev = JsonSerializer.Deserialize<ForecastScenarioCreated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(ForecastScenarioModified))
                ev = JsonSerializer.Deserialize<ForecastScenarioModified>(e.PayloadJson, opt);
            else if (e.EventType == nameof(ForecastScenarioArchived))
                ev = JsonSerializer.Deserialize<ForecastScenarioArchived>(e.PayloadJson, opt);
            else if (e.EventType == nameof(ForecastScenarioChangeAdded))
                ev = JsonSerializer.Deserialize<ForecastScenarioChangeAdded>(e.PayloadJson, opt);
            else if (e.EventType == nameof(ForecastScenarioChangeRemoved))
                ev = JsonSerializer.Deserialize<ForecastScenarioChangeRemoved>(e.PayloadJson, opt);

            // --- Party events ---
            else if (e.EventType == nameof(PartyCreated))
                ev = JsonSerializer.Deserialize<PartyCreated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(PartyModified))
                ev = JsonSerializer.Deserialize<PartyModified>(e.PayloadJson, opt);
            else if (e.EventType == nameof(PartyArchived))
                ev = JsonSerializer.Deserialize<PartyArchived>(e.PayloadJson, opt);

            // --- Contract events ---
            else if (e.EventType == nameof(ContractCreated))
                ev = JsonSerializer.Deserialize<ContractCreated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(ContractModified))
                ev = JsonSerializer.Deserialize<ContractModified>(e.PayloadJson, opt);
            else if (e.EventType == nameof(ContractArchived))
                ev = JsonSerializer.Deserialize<ContractArchived>(e.PayloadJson, opt);

            // --- Billing events ---
            else if (e.EventType == nameof(BillIssued))
                ev = JsonSerializer.Deserialize<BillIssued>(e.PayloadJson, opt);
            else if (e.EventType == nameof(InvoiceIssued))
                ev = JsonSerializer.Deserialize<InvoiceIssued>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BillCancelled))
                ev = JsonSerializer.Deserialize<BillCancelled>(e.PayloadJson, opt);
            else if (e.EventType == nameof(InvoiceCancelled))
                ev = JsonSerializer.Deserialize<InvoiceCancelled>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BillDisputed))
                ev = JsonSerializer.Deserialize<BillDisputed>(e.PayloadJson, opt);
            else if (e.EventType == nameof(InvoiceDisputed))
                ev = JsonSerializer.Deserialize<InvoiceDisputed>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BillWrittenOff))
                ev = JsonSerializer.Deserialize<BillWrittenOff>(e.PayloadJson, opt);
            else if (e.EventType == nameof(InvoiceWrittenOff))
                ev = JsonSerializer.Deserialize<InvoiceWrittenOff>(e.PayloadJson, opt);

            // --- Billing adjustment events ---
            else if (e.EventType == nameof(BillAdjustmentAdded))
                ev = JsonSerializer.Deserialize<BillAdjustmentAdded>(e.PayloadJson, opt);
            else if (e.EventType == nameof(InvoiceAdjustmentAdded))
                ev = JsonSerializer.Deserialize<InvoiceAdjustmentAdded>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BillAdjustmentReversed))
                ev = JsonSerializer.Deserialize<BillAdjustmentReversed>(e.PayloadJson, opt);
            else if (e.EventType == nameof(InvoiceAdjustmentReversed))
                ev = JsonSerializer.Deserialize<InvoiceAdjustmentReversed>(e.PayloadJson, opt);

            // --- Billing payment events ---
            else if (e.EventType == nameof(BillPaymentRecorded))
                ev = JsonSerializer.Deserialize<BillPaymentRecorded>(e.PayloadJson, opt);
            else if (e.EventType == nameof(InvoicePaymentRecorded))
                ev = JsonSerializer.Deserialize<InvoicePaymentRecorded>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BillPaymentReversed))
                ev = JsonSerializer.Deserialize<BillPaymentReversed>(e.PayloadJson, opt);
            else if (e.EventType == nameof(InvoicePaymentReversed))
                ev = JsonSerializer.Deserialize<InvoicePaymentReversed>(e.PayloadJson, opt);
            else if (e.EventType == nameof(BillPaymentUnapplied))
                ev = JsonSerializer.Deserialize<BillPaymentUnapplied>(e.PayloadJson, opt);
            else if (e.EventType == nameof(InvoicePaymentUnapplied))
                ev = JsonSerializer.Deserialize<InvoicePaymentUnapplied>(e.PayloadJson, opt);

            // --- Billing generation events ---
            else if (e.EventType == nameof(ContractBillGenerated))
                ev = JsonSerializer.Deserialize<ContractBillGenerated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(ContractInvoiceGenerated))
                ev = JsonSerializer.Deserialize<ContractInvoiceGenerated>(e.PayloadJson, opt);

            // --- Notification events ---
            else if (e.EventType == nameof(NotificationRuleCreated))
                ev = JsonSerializer.Deserialize<NotificationRuleCreated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(NotificationRuleModified))
                ev = JsonSerializer.Deserialize<NotificationRuleModified>(e.PayloadJson, opt);
            else if (e.EventType == nameof(NotificationRuleArchived))
                ev = JsonSerializer.Deserialize<NotificationRuleArchived>(e.PayloadJson, opt);
            else if (e.EventType == nameof(NotificationAcknowledged))
                ev = JsonSerializer.Deserialize<NotificationAcknowledged>(e.PayloadJson, opt);
            else if (e.EventType == nameof(NotificationDismissed))
                ev = JsonSerializer.Deserialize<NotificationDismissed>(e.PayloadJson, opt);
            else if (e.EventType == nameof(NotificationSnoozed))
                ev = JsonSerializer.Deserialize<NotificationSnoozed>(e.PayloadJson, opt);
            else if (e.EventType == nameof(NotificationActionLinked))
                ev = JsonSerializer.Deserialize<NotificationActionLinked>(e.PayloadJson, opt);

            // --- Import Rule Pack events ---
            else if (e.EventType == nameof(ImportRulePackCreated))
                ev = JsonSerializer.Deserialize<ImportRulePackCreated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(ImportRulePackModified))
                ev = JsonSerializer.Deserialize<ImportRulePackModified>(e.PayloadJson, opt);
            else if (e.EventType == nameof(ImportRulePackArchived))
                ev = JsonSerializer.Deserialize<ImportRulePackArchived>(e.PayloadJson, opt);
            else if (e.EventType == nameof(ImportRuleDefined))
                ev = JsonSerializer.Deserialize<ImportRuleDefined>(e.PayloadJson, opt);
            else if (e.EventType == nameof(ImportRuleArchived))
                ev = JsonSerializer.Deserialize<ImportRuleArchived>(e.PayloadJson, opt);
            else if (e.EventType == nameof(ImportRuleTested))
                ev = JsonSerializer.Deserialize<ImportRuleTested>(e.PayloadJson, opt);
            else if (e.EventType == nameof(ImportAutoActionExecuted))
                ev = JsonSerializer.Deserialize<ImportAutoActionExecuted>(e.PayloadJson, opt);

            // --- AI Advisor events ---
            else if (e.EventType == nameof(AiInsightRecorded))
                ev = JsonSerializer.Deserialize<AiInsightRecorded>(e.PayloadJson, opt);
            else if (e.EventType == nameof(AiProposalCreated))
                ev = JsonSerializer.Deserialize<AiProposalCreated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(AiProposalApproved))
                ev = JsonSerializer.Deserialize<AiProposalApproved>(e.PayloadJson, opt);
            else if (e.EventType == nameof(AiProposalRejected))
                ev = JsonSerializer.Deserialize<AiProposalRejected>(e.PayloadJson, opt);
            else if (e.EventType == nameof(AiSettingsUpdated))
                ev = JsonSerializer.Deserialize<AiSettingsUpdated>(e.PayloadJson, opt);

            // --- Identity events ---
            else if (e.EventType == nameof(VaultUserCreated))
                ev = JsonSerializer.Deserialize<VaultUserCreated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(VaultUserModified))
                ev = JsonSerializer.Deserialize<VaultUserModified>(e.PayloadJson, opt);
            else if (e.EventType == nameof(VaultUserArchived))
                ev = JsonSerializer.Deserialize<VaultUserArchived>(e.PayloadJson, opt);
            else if (e.EventType == nameof(VaultUserSecretSet))
                ev = JsonSerializer.Deserialize<VaultUserSecretSet>(e.PayloadJson, opt);
            else if (e.EventType == nameof(VaultUserSecretRotated))
                ev = JsonSerializer.Deserialize<VaultUserSecretRotated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(UserSessionStarted))
                ev = JsonSerializer.Deserialize<UserSessionStarted>(e.PayloadJson, opt);
            else if (e.EventType == nameof(UserSessionEnded))
                ev = JsonSerializer.Deserialize<UserSessionEnded>(e.PayloadJson, opt);
            else if (e.EventType == nameof(PermissionOverrideGranted))
                ev = JsonSerializer.Deserialize<PermissionOverrideGranted>(e.PayloadJson, opt);
            else if (e.EventType == nameof(PermissionOverrideRevoked))
                ev = JsonSerializer.Deserialize<PermissionOverrideRevoked>(e.PayloadJson, opt);

            // --- Document Vault events ---
            else if (e.EventType == nameof(DocumentCreated))
                ev = JsonSerializer.Deserialize<DocumentCreated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(DocumentMetadataUpdated))
                ev = JsonSerializer.Deserialize<DocumentMetadataUpdated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(DocumentArchived))
                ev = JsonSerializer.Deserialize<DocumentArchived>(e.PayloadJson, opt);
            else if (e.EventType == nameof(DocumentBlobPurged))
                ev = JsonSerializer.Deserialize<DocumentBlobPurged>(e.PayloadJson, opt);
            else if (e.EventType == nameof(DocumentLinked))
                ev = JsonSerializer.Deserialize<DocumentLinked>(e.PayloadJson, opt);
            else if (e.EventType == nameof(DocumentUnlinked))
                ev = JsonSerializer.Deserialize<DocumentUnlinked>(e.PayloadJson, opt);
            else if (e.EventType == nameof(DocumentExported))
                ev = JsonSerializer.Deserialize<DocumentExported>(e.PayloadJson, opt);

            // --- Income Source events ---
            else if (e.EventType == nameof(IncomeSourceDefined))
                ev = JsonSerializer.Deserialize<IncomeSourceDefined>(e.PayloadJson, opt);
            else if (e.EventType == nameof(IncomeSourceArchived))
                ev = JsonSerializer.Deserialize<IncomeSourceArchived>(e.PayloadJson, opt);

            // --- Tag events ---
            else if (e.EventType == nameof(EntityTagsReplaced))
                ev = JsonSerializer.Deserialize<EntityTagsReplaced>(e.PayloadJson, opt);

            // --- Vault registry events (global) ---
            else if (e.EventType == nameof(VaultCreated))
                ev = JsonSerializer.Deserialize<VaultCreated>(e.PayloadJson, opt);
            else if (e.EventType == nameof(VaultRenamed))
                ev = JsonSerializer.Deserialize<VaultRenamed>(e.PayloadJson, opt);
            else if (e.EventType == nameof(VaultArchived))
                ev = JsonSerializer.Deserialize<VaultArchived>(e.PayloadJson, opt);
            else if (e.EventType == nameof(ActiveVaultSelected))
                ev = JsonSerializer.Deserialize<ActiveVaultSelected>(e.PayloadJson, opt);

            if (ev is not null)
                yield return ev;
        }
    }

    public static IEnumerable<ScheduleDefinition> ToSchedules(IEnumerable<EventEnvelope> envelopes)
    {
        foreach (var e in envelopes)
        {
            if (e.EventType != "ScheduleDefined") continue;

            var def = JsonSerializer.Deserialize<ScheduleDefinition>(e.PayloadJson, DomainJson.Options);
            if (def is null) continue;

            yield return def;
        }
    }
}
