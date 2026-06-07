using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// ========== Commands ==========

public sealed record RevertImportedDecisionCommand(
    Guid ImportedId,
    DateOnly EffectiveDate,
    string Reason
);

public sealed record CorrectImportedDecisionCommand(
    Guid ImportedId,
    string NewDecisionType,
    string? ApplyMode,
    Guid? TargetId,
    DateOnly EffectiveDate,
    string Reason
);

public sealed record UndoImportBatchCommand(
    Guid BatchId,
    DateOnly EffectiveDate,
    string Reason
);

public sealed record BulkApplyUnmatchedCommand(
    Guid BatchId,
    Guid AccountId,
    DateOnly EffectiveDate,
    string Reason,
    bool UseAutoCategoryRules
);

// ========== Handlers ==========

public sealed class RevertImportedDecisionHandler
{
    private readonly IEventStore _store;
    public RevertImportedDecisionHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(RevertImportedDecisionCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var importState = BankImportProjector.Project(all);

        if (!importState.ImportedTransactions.TryGetValue(cmd.ImportedId, out var imported))
            throw new InvalidOperationException("Imported transaction not found.");

        var decision = importState.Decisions.GetValueOrDefault(cmd.ImportedId);
        if (decision == null || decision.CurrentStatus == ImportedTransactionStatus.Unresolved)
            throw new InvalidOperationException("Nothing to revert.");

        var correlationId = Guid.NewGuid();
        var revertedType = decision.CurrentStatus switch
        {
            ImportedTransactionStatus.Applied => "applied",
            ImportedTransactionStatus.Matched => "matched",
            ImportedTransactionStatus.Ignored => "ignored",
            _ => "unknown"
        };

        // If the decision was "applied", emit cash reversal events
        if (decision.CurrentStatus == ImportedTransactionStatus.Applied && decision.ActiveAppliedEventId.HasValue)
        {
            await EmitCashReversalAsync(
                all, imported, decision.ActiveAppliedEventId.Value, decision.ActiveAppliedType!,
                cmd.EffectiveDate, cmd.Reason, actorUserId, deviceId, correlationId, ct);
        }

        // Emit the revert event
        var revertEvent = new BankTransactionDecisionReverted(
            cmd.ImportedId, revertedType, decision.ActiveAppliedEventId, cmd.EffectiveDate, cmd.Reason);

        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportStreams.ImportStream,
            nameof(BankTransactionDecisionReverted), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(revertEvent, DomainJson.Options)
        ), ct);
    }

    private async Task EmitCashReversalAsync(
        IReadOnlyList<EventEnvelope> allEvents,
        ImportedTransaction imported,
        Guid appliedEventId,
        string appliedType,
        DateOnly effectiveDate,
        string reason,
        Guid actorUserId,
        Guid deviceId,
        Guid correlationId,
        CancellationToken ct)
    {
        switch (appliedType)
        {
            case "IncomeRecorded":
            {
                var reversal = new IncomeReversed(appliedEventId, imported.AccountId, imported.Amount, effectiveDate, reason);
                await _store.AppendAsync(new EventEnvelope(
                    new EventId(Guid.NewGuid()), new StreamId(imported.AccountId),
                    nameof(IncomeReversed), DateTimeOffset.UtcNow, effectiveDate,
                    actorUserId, deviceId, correlationId, null, 1,
                    JsonSerializer.Serialize(reversal, DomainJson.Options)
                ), ct);
                break;
            }
            case "ExpenseRecorded":
            {
                var reversal = new ExpenseReversed(appliedEventId, imported.AccountId, imported.Amount, effectiveDate, reason);
                await _store.AppendAsync(new EventEnvelope(
                    new EventId(Guid.NewGuid()), new StreamId(imported.AccountId),
                    nameof(ExpenseReversed), DateTimeOffset.UtcNow, effectiveDate,
                    actorUserId, deviceId, correlationId, null, 1,
                    JsonSerializer.Serialize(reversal, DomainJson.Options)
                ), ct);
                break;
            }
            case "TransferRecorded":
            {
                // Find the original transfer event to get FromAccountId and ToAccountId
                var origEnvelope = allEvents.FirstOrDefault(e => e.EventId.Value == appliedEventId);
                if (origEnvelope != null)
                {
                    var origTransfer = JsonSerializer.Deserialize<TransferRecorded>(origEnvelope.PayloadJson, DomainJson.Options);
                    if (origTransfer != null)
                    {
                        var reversal = new TransferReversed(appliedEventId, origTransfer.FromAccountId, origTransfer.ToAccountId,
                            origTransfer.Amount, effectiveDate, reason);
                        await _store.AppendAsync(new EventEnvelope(
                            new EventId(Guid.NewGuid()), new StreamId(origTransfer.FromAccountId),
                            nameof(TransferReversed), DateTimeOffset.UtcNow, effectiveDate,
                            actorUserId, deviceId, correlationId, null, 1,
                            JsonSerializer.Serialize(reversal, DomainJson.Options)
                        ), ct);
                    }
                }
                break;
            }
        }
    }
}

public sealed class CorrectImportedDecisionHandler
{
    private readonly IEventStore _store;
    public CorrectImportedDecisionHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(CorrectImportedDecisionCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var importState = BankImportProjector.Project(all);

        if (!importState.ImportedTransactions.TryGetValue(cmd.ImportedId, out var imported))
            throw new InvalidOperationException("Imported transaction not found.");

        var decision = importState.Decisions.GetValueOrDefault(cmd.ImportedId);

        // Block if already the exact same active decision
        if (decision != null)
        {
            var targetStatus = cmd.NewDecisionType switch
            {
                "apply" => ImportedTransactionStatus.Applied,
                "match" => ImportedTransactionStatus.Matched,
                "ignore" => ImportedTransactionStatus.Ignored,
                _ => ImportedTransactionStatus.Unresolved
            };
            if (decision.CurrentStatus == targetStatus)
            {
                // For apply, allow correction if mode differs (e.g. Expense -> Income)
                if (targetStatus == ImportedTransactionStatus.Applied && cmd.ApplyMode != null)
                {
                    var currentAppliedType = decision.ActiveAppliedType ?? "";
                    var targetAppliedType = cmd.ApplyMode + "Recorded";
                    if (!string.Equals(currentAppliedType, targetAppliedType, StringComparison.OrdinalIgnoreCase))
                    {
                        // Different apply mode — allow correction
                    }
                    else
                    {
                        throw new InvalidOperationException("Already in the requested decision state.");
                    }
                }
                else
                {
                    throw new InvalidOperationException("Already in the requested decision state.");
                }
            }
        }

        var correlationId = Guid.NewGuid();

        // Step 1: Revert current decision if active
        if (decision != null && decision.CurrentStatus != ImportedTransactionStatus.Unresolved)
        {
            var revertHandler = new RevertImportedDecisionHandler(_store);
            await revertHandler.HandleAsync(
                new RevertImportedDecisionCommand(cmd.ImportedId, cmd.EffectiveDate,
                    $"Correction: {cmd.Reason}"),
                actorUserId, deviceId, ct);
        }

        // Step 2: Emit correction event
        var corrEvent = new BankTransactionDecisionCorrected(
            cmd.ImportedId, cmd.NewDecisionType, cmd.ApplyMode, cmd.TargetId, cmd.EffectiveDate, cmd.Reason);

        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportStreams.ImportStream,
            nameof(BankTransactionDecisionCorrected), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(corrEvent, DomainJson.Options)
        ), ct);

        // Step 3: Execute the new decision
        switch (cmd.NewDecisionType)
        {
            case "apply":
            {
                var applyHandler = new ApplyImportedTransactionHandler(_store);
                await applyHandler.HandleAsync(
                    new ApplyImportedTransactionCommand(cmd.ImportedId, cmd.ApplyMode ?? "Expense", null, null, cmd.TargetId),
                    actorUserId, deviceId, ct);
                break;
            }
            case "match":
            {
                if (!cmd.TargetId.HasValue)
                    throw new InvalidOperationException("Match correction requires TargetId (MatchedEventId).");
                var matchHandler = new ConfirmMatchImportedTransactionHandler(_store);
                await matchHandler.HandleAsync(
                    new ConfirmMatchImportedTransactionCommand(cmd.ImportedId, cmd.TargetId.Value, cmd.Reason),
                    actorUserId, deviceId, ct);
                break;
            }
            case "ignore":
            {
                var ignoreHandler = new IgnoreImportedTransactionHandler(_store);
                await ignoreHandler.HandleAsync(
                    new IgnoreImportedTransactionCommand(cmd.ImportedId, cmd.Reason),
                    actorUserId, deviceId, ct);
                break;
            }
            default:
                throw new InvalidOperationException($"Unknown decision type: {cmd.NewDecisionType}");
        }
    }
}

public sealed class UndoImportBatchHandler
{
    private readonly IEventStore _store;
    public UndoImportBatchHandler(IEventStore store) => _store = store;

    public async Task<int> HandleAsync(UndoImportBatchCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var importState = BankImportProjector.Project(all);

        if (!importState.Batches.ContainsKey(cmd.BatchId))
            throw new InvalidOperationException("Batch not found.");

        var correlationId = Guid.NewGuid();

        // Emit batch undo requested
        var requestEvent = new BankImportBatchUndoRequested(cmd.BatchId, cmd.EffectiveDate, cmd.Reason);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportStreams.ImportStream,
            nameof(BankImportBatchUndoRequested), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(requestEvent, DomainJson.Options)
        ), ct);

        // Enumerate all imported transactions in that batch with active decisions
        var batchImportedIds = importState.ImportedTransactions.Values
            .Where(t => t.BatchId == cmd.BatchId)
            .Select(t => t.ImportedId)
            .ToList();

        int revertedCount = 0;
        var revertHandler = new RevertImportedDecisionHandler(_store);

        foreach (var importedId in batchImportedIds)
        {
            var decision = importState.Decisions.GetValueOrDefault(importedId);
            if (decision == null || decision.CurrentStatus == ImportedTransactionStatus.Unresolved)
                continue;

            // Re-read state for each to pick up prior reverts in this batch
            await revertHandler.HandleAsync(
                new RevertImportedDecisionCommand(importedId, cmd.EffectiveDate, $"Batch undo: {cmd.Reason}"),
                actorUserId, deviceId, ct);
            revertedCount++;
        }

        // Emit batch undo completed
        var completedEvent = new BankImportBatchUndoCompleted(cmd.BatchId, revertedCount, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportStreams.ImportStream,
            nameof(BankImportBatchUndoCompleted), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(completedEvent, DomainJson.Options)
        ), ct);

        return revertedCount;
    }
}

public sealed class BulkApplyUnmatchedHandler
{
    private readonly IEventStore _store;
    public BulkApplyUnmatchedHandler(IEventStore store) => _store = store;

    public async Task<int> HandleAsync(BulkApplyUnmatchedCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var importState = BankImportProjector.Project(all);

        if (!importState.Batches.ContainsKey(cmd.BatchId))
            throw new InvalidOperationException("Batch not found.");

        var unresolvedRows = importState.ImportedTransactions.Values
            .Where(t => t.BatchId == cmd.BatchId && t.AccountId == cmd.AccountId)
            .Where(t =>
            {
                var d = importState.Decisions.GetValueOrDefault(t.ImportedId);
                return d == null || d.CurrentStatus == ImportedTransactionStatus.Unresolved;
            })
            .ToList();

        int appliedCount = 0;
        var applyHandler = new ApplyImportedTransactionHandler(_store);

        foreach (var txn in unresolvedRows)
        {
            var mode = txn.Direction == "credit" ? "Income" : "Expense";
            string? categoryId = null;

            if (cmd.UseAutoCategoryRules)
            {
                categoryId = InferCategory(txn.Description, txn.Counterparty);
            }

            await applyHandler.HandleAsync(
                new ApplyImportedTransactionCommand(txn.ImportedId, mode, categoryId, txn.Description, null),
                actorUserId, deviceId, ct);
            appliedCount++;
        }

        return appliedCount;
    }

    private static string? InferCategory(string description, string counterparty)
    {
        var text = $"{description} {counterparty}".ToLowerInvariant();

        if (text.Contains("grocery") || text.Contains("supermarket") || text.Contains("food"))
            return "Groceries";
        if (text.Contains("salary") || text.Contains("payroll"))
            return "Salary";
        if (text.Contains("rent"))
            return "Rent";
        if (text.Contains("electric") || text.Contains("water") || text.Contains("gas") || text.Contains("utility"))
            return "Utilities";
        if (text.Contains("transfer"))
            return "Transfer";
        if (text.Contains("fuel") || text.Contains("gas station") || text.Contains("petrol"))
            return "Transportation";

        return null;
    }
}
