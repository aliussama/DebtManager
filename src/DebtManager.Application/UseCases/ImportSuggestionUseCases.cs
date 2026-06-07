using System.Text.Json;
using DebtManager.Application.Projections;
using DebtManager.Domain.Events;
using DebtManager.Domain.ImportRules;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// ????????????????????????????????????????????
// DTOs
// ????????????????????????????????????????????

public sealed record ImportSuggestionDto(
    string SuggestionId,
    Guid ImportedTransactionId,
    string Kind,
    int Confidence,
    Guid? ProposedAccountId,
    string? ProposedCategory,
    Guid? ProposedRelatedEntityId,
    string? Notes,
    List<string> ExplanationLines);

public sealed record ApplySuggestionCommand(
    Guid ImportedTransactionId,
    string SuggestionId,
    string ActionKind,
    Guid? ProposedAccountId,
    string? ProposedCategory,
    Guid? ProposedRelatedEntityId,
    string? Notes,
    bool IsAutoApply);

public sealed record RunAutoApplyCommand(Guid BatchId, bool AutoApplyEnabled, int MinConfidenceThreshold, DateOnly AsOfDate);

// ????????????????????????????????????????????
// Get Suggestions
// ????????????????????????????????????????????

public sealed class GetImportSuggestionsHandler
{
    private readonly IEventStore _store;
    private readonly ProjectionRunner? _runner;

    public GetImportSuggestionsHandler(IEventStore store, ProjectionRunner? runner = null)
    {
        _store = store;
        _runner = runner;
    }

    public async Task<List<ImportSuggestionDto>> HandleAsync(Guid batchId, DateOnly asOfDate, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var importState = BankImportProjector.Project(all);
        var rulesState = ImportRulesProjector.Project(all);
        var billingState = BillingProjector.Project(all, asOfDate);

        CashLedgerState ledgerState;
        CategoryState categoryState;

        if (_runner != null)
        {
            ledgerState = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
                e => CashLedgerProjector.Project(e), ct: ct);
            categoryState = await _runner.RunAsync(
                "CategoryState",
                e => CategoryProjector.Project(e), ct: ct);
        }
        else
        {
            ledgerState = CashLedgerProjector.Project(all);
            categoryState = CategoryProjector.Project(all);
        }

        var activeRules = rulesState.GetActiveRulesFlattened();
        var batchTxns = importState.ImportedTransactions.Values
            .Where(t => t.BatchId == batchId)
            .OrderBy(t => t.ImportedId);

        var results = new List<ImportSuggestionDto>();

        foreach (var txn in batchTxns)
        {
            // Skip already-resolved transactions
            if (importState.Decisions.TryGetValue(txn.ImportedId, out var decision) &&
                decision.CurrentStatus != ImportedTransactionStatus.Unresolved)
                continue;

            var suggestions = ImportRuleEngine.Evaluate(txn, activeRules, billingState, ledgerState, categoryState);

            foreach (var s in suggestions)
            {
                results.Add(new ImportSuggestionDto(
                    s.DeterministicSuggestionId,
                    s.ImportedTransactionId,
                    s.Kind.ToString(),
                    s.Confidence,
                    s.ProposedAccountId,
                    s.ProposedCategory,
                    s.ProposedRelatedEntityId,
                    s.Notes,
                    s.Explain.Select(e => e.ExplanationText).ToList()));
            }
        }

        return results;
    }
}

// ????????????????????????????????????????????
// Apply a single suggestion
// ????????????????????????????????????????????

public sealed class ApplySuggestionHandler
{
    private readonly IEventStore _store;
    private readonly ApplyImportedTransactionHandler _applyHandler;
    private readonly ConfirmMatchImportedTransactionHandler _matchHandler;
    private readonly IgnoreImportedTransactionHandler _ignoreHandler;

    public ApplySuggestionHandler(
        IEventStore store,
        ApplyImportedTransactionHandler applyHandler,
        ConfirmMatchImportedTransactionHandler matchHandler,
        IgnoreImportedTransactionHandler ignoreHandler)
    {
        _store = store;
        _applyHandler = applyHandler;
        _matchHandler = matchHandler;
        _ignoreHandler = ignoreHandler;
    }

    public async Task HandleAsync(ApplySuggestionCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var kind = Enum.TryParse<SuggestionKind>(cmd.ActionKind, out var k) ? k : SuggestionKind.MatchOnly;

        switch (kind)
        {
            case SuggestionKind.ApplyExpense:
                await _applyHandler.HandleAsync(
                    new ApplyImportedTransactionCommand(cmd.ImportedTransactionId, "Expense", cmd.ProposedCategory, cmd.Notes, null),
                    actorUserId, deviceId, ct);
                break;

            case SuggestionKind.ApplyIncome:
                await _applyHandler.HandleAsync(
                    new ApplyImportedTransactionCommand(cmd.ImportedTransactionId, "Income", null, cmd.Notes, null),
                    actorUserId, deviceId, ct);
                break;

            case SuggestionKind.ApplyTransfer:
                await _applyHandler.HandleAsync(
                    new ApplyImportedTransactionCommand(cmd.ImportedTransactionId, "Transfer", null, cmd.Notes, cmd.ProposedAccountId),
                    actorUserId, deviceId, ct);
                break;

            case SuggestionKind.Ignore:
                await _ignoreHandler.HandleAsync(
                    new IgnoreImportedTransactionCommand(cmd.ImportedTransactionId, cmd.Notes ?? "Auto-ignored by rule"),
                    actorUserId, deviceId, ct);
                break;

            case SuggestionKind.MatchOnly:
                if (cmd.ProposedRelatedEntityId.HasValue)
                    await _matchHandler.HandleAsync(
                        new ConfirmMatchImportedTransactionCommand(cmd.ImportedTransactionId, cmd.ProposedRelatedEntityId.Value, cmd.Notes),
                        actorUserId, deviceId, ct);
                break;

            case SuggestionKind.PayBill:
                await _applyHandler.HandleAsync(
                    new ApplyImportedTransactionCommand(cmd.ImportedTransactionId, "Expense",
                        "BillPayment", cmd.Notes, null),
                    actorUserId, deviceId, ct);
                break;

            case SuggestionKind.ReceiveInvoice:
                await _applyHandler.HandleAsync(
                    new ApplyImportedTransactionCommand(cmd.ImportedTransactionId, "Income",
                        "InvoicePayment", cmd.Notes, null),
                    actorUserId, deviceId, ct);
                break;

            case SuggestionKind.Categorize:
                var mode = cmd.Notes?.Contains("credit") == true ? "Income" : "Expense";
                await _applyHandler.HandleAsync(
                    new ApplyImportedTransactionCommand(cmd.ImportedTransactionId, mode, cmd.ProposedCategory, cmd.Notes, null),
                    actorUserId, deviceId, ct);
                break;
        }

        // If this was an auto-apply action, emit an audit event
        if (cmd.IsAutoApply)
        {
            var batchId = await GetBatchIdForTransaction(cmd.ImportedTransactionId, ct);
            var ev = new ImportAutoActionExecuted(
                batchId,
                cmd.ImportedTransactionId,
                kind == SuggestionKind.Ignore ? "AutoIgnored" : "AutoApplied",
                cmd.ProposedRelatedEntityId,
                $"Auto-applied suggestion {cmd.SuggestionId}",
                DateOnly.FromDateTime(DateTime.Today));

            await _store.AppendAsync(new EventEnvelope(
                new EventId(Guid.NewGuid()), ImportStreams.ImportStream,
                nameof(ImportAutoActionExecuted), DateTimeOffset.UtcNow, ev.EffectiveDate,
                actorUserId, deviceId, Guid.NewGuid(), null, 1,
                JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
        }
    }

    private async Task<Guid> GetBatchIdForTransaction(Guid importedId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var importState = BankImportProjector.Project(all);
        return importState.ImportedTransactions.TryGetValue(importedId, out var txn) ? txn.BatchId : Guid.Empty;
    }
}

// ????????????????????????????????????????????
// Auto-apply for batch
// ????????????????????????????????????????????

public sealed class RunAutoApplyForBatchHandler
{
    private readonly IEventStore _store;
    private readonly GetImportSuggestionsHandler _suggestionsHandler;
    private readonly ApplySuggestionHandler _applySuggestionHandler;

    public RunAutoApplyForBatchHandler(
        IEventStore store,
        GetImportSuggestionsHandler suggestionsHandler,
        ApplySuggestionHandler applySuggestionHandler)
    {
        _store = store;
        _suggestionsHandler = suggestionsHandler;
        _applySuggestionHandler = applySuggestionHandler;
    }

    public async Task<int> HandleAsync(RunAutoApplyCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        if (!cmd.AutoApplyEnabled)
            return 0;

        var suggestions = await _suggestionsHandler.HandleAsync(cmd.BatchId, cmd.AsOfDate, ct);

        // Group by ImportedTransactionId, take highest confidence
        var bestPerTxn = suggestions
            .GroupBy(s => s.ImportedTransactionId)
            .Select(g => g.OrderByDescending(s => s.Confidence).First())
            .Where(s => s.Confidence >= cmd.MinConfidenceThreshold)
            .OrderBy(s => s.ImportedTransactionId) // deterministic order
            .ToList();

        int applied = 0;

        foreach (var s in bestPerTxn)
        {
            try
            {
                await _applySuggestionHandler.HandleAsync(
                    new ApplySuggestionCommand(
                        s.ImportedTransactionId,
                        s.SuggestionId,
                        s.Kind,
                        s.ProposedAccountId,
                        s.ProposedCategory,
                        s.ProposedRelatedEntityId,
                        s.Notes,
                        IsAutoApply: true),
                    actorUserId, deviceId, ct);
                applied++;
            }
            catch (InvalidOperationException)
            {
                // Already resolved — idempotent skip
            }
        }

        return applied;
    }
}
