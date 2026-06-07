using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

public sealed class ImportProfileItem
{
    public Guid ProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MappingJson { get; set; } = "{}";
    public bool IsArchived { get; set; }
}

public sealed class ImportBatchItem
{
    public Guid BatchId { get; set; }
    public Guid ProfileId { get; set; }
    public Guid AccountId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileHashSha256 { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public bool IsCompleted { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedDuplicatesCount { get; set; }
}

public sealed class ImportedTransaction
{
    public Guid ImportedId { get; set; }
    public Guid BatchId { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly TxnDate { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Counterparty { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string RawJson { get; set; } = string.Empty;
}

public sealed class MatchLink
{
    public Guid ImportedId { get; set; }
    public Guid MatchedEventId { get; set; }
    public string MatchType { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
}

public sealed class AppliedLink
{
    public Guid ImportedId { get; set; }
    public Guid AppliedEventId { get; set; }
    public string AppliedType { get; set; } = string.Empty;
}

public enum ImportedTransactionStatus
{
    Unresolved,
    Matched,
    Ignored,
    Applied
}

public sealed class ImportedTransactionDecision
{
    public Guid ImportedId { get; set; }
    public ImportedTransactionStatus CurrentStatus { get; set; } = ImportedTransactionStatus.Unresolved;
    public Guid? ActiveAppliedEventId { get; set; }
    public string? ActiveAppliedType { get; set; }
    public Guid? ActiveMatchedEventId { get; set; }
    public string? ActiveMatchType { get; set; }
    public decimal ActiveConfidence { get; set; }
}

/// <summary>
/// Full bank import state derived from events.
/// </summary>
public sealed class BankImportState
{
    public Dictionary<Guid, ImportProfileItem> Profiles { get; } = new();
    public Dictionary<Guid, ImportBatchItem> Batches { get; } = new();
    public Dictionary<Guid, ImportedTransaction> ImportedTransactions { get; } = new();
    public Dictionary<Guid, ImportedTransactionDecision> Decisions { get; } = new();

    // Legacy accessors derived from Decisions for backward compatibility
    public Dictionary<Guid, MatchLink> MatchedLinks { get; } = new();
    public Dictionary<Guid, AppliedLink> AppliedLinks { get; } = new();
    public HashSet<Guid> IgnoredIds { get; } = new();

    /// <summary>
    /// Check if a file hash has already been imported for a given account+profile.
    /// </summary>
    public bool IsDuplicateBatch(Guid profileId, Guid accountId, string fileHash) =>
        Batches.Values.Any(b =>
            b.ProfileId == profileId &&
            b.AccountId == accountId &&
            b.FileHashSha256 == fileHash &&
            b.IsCompleted);

    /// <summary>
    /// Check if an imported transaction is a duplicate within existing imports
    /// by matching date+amount+description for same account.
    /// </summary>
    public bool IsDuplicateTransaction(Guid accountId, DateOnly txnDate, decimal amount, string description) =>
        ImportedTransactions.Values.Any(t =>
            t.AccountId == accountId &&
            t.TxnDate == txnDate &&
            t.Amount == amount &&
            t.Description == description);
}

/// <summary>
/// Projects bank import events into BankImportState.
/// </summary>
public static class BankImportProjector
{
    public static BankImportState Project(IEnumerable<EventEnvelope> envelopes)
    {
        var state = new BankImportState();
        var opt = DomainJson.Options;

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value);

        foreach (var env in ordered)
        {
            switch (env.EventType)
            {
                case nameof(BankImportProfileCreated):
                {
                    var ev = JsonSerializer.Deserialize<BankImportProfileCreated>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Profiles[ev.ProfileId] = new ImportProfileItem
                    {
                        ProfileId = ev.ProfileId,
                        Name = ev.Name,
                        MappingJson = ev.MappingJson
                    };
                    break;
                }
                case nameof(BankImportProfileModified):
                {
                    var ev = JsonSerializer.Deserialize<BankImportProfileModified>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Profiles.TryGetValue(ev.ProfileId, out var p))
                        p.MappingJson = ev.MappingJson;
                    break;
                }
                case nameof(BankImportProfileArchived):
                {
                    var ev = JsonSerializer.Deserialize<BankImportProfileArchived>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Profiles.TryGetValue(ev.ProfileId, out var p))
                        p.IsArchived = true;
                    break;
                }
                case nameof(BankImportBatchStarted):
                {
                    var ev = JsonSerializer.Deserialize<BankImportBatchStarted>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Batches[ev.BatchId] = new ImportBatchItem
                    {
                        BatchId = ev.BatchId,
                        ProfileId = ev.ProfileId,
                        AccountId = ev.AccountId,
                        FileName = ev.FileName,
                        FileHashSha256 = ev.FileHashSha256,
                        RowCount = ev.RowCount
                    };
                    break;
                }
                case nameof(BankTransactionImported):
                {
                    var ev = JsonSerializer.Deserialize<BankTransactionImported>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.ImportedTransactions[ev.ImportedId] = new ImportedTransaction
                    {
                        ImportedId = ev.ImportedId,
                        BatchId = ev.BatchId,
                        AccountId = ev.AccountId,
                        TxnDate = ev.TxnDate,
                        Amount = ev.Amount,
                        CurrencyCode = ev.CurrencyCode,
                        Description = ev.Description,
                        Reference = ev.Reference,
                        Counterparty = ev.Counterparty,
                        Direction = ev.Direction,
                        RawJson = ev.RawJson
                    };
                    break;
                }
                case nameof(BankImportBatchCompleted):
                {
                    var ev = JsonSerializer.Deserialize<BankImportBatchCompleted>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Batches.TryGetValue(ev.BatchId, out var b))
                    {
                        b.IsCompleted = true;
                        b.ImportedCount = ev.ImportedCount;
                        b.SkippedDuplicatesCount = ev.SkippedDuplicatesCount;
                    }
                    break;
                }
                case nameof(BankTransactionMatched):
                {
                    var ev = JsonSerializer.Deserialize<BankTransactionMatched>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.MatchedLinks[ev.ImportedId] = new MatchLink
                    {
                        ImportedId = ev.ImportedId,
                        MatchedEventId = ev.MatchedEventId,
                        MatchType = ev.MatchType,
                        Confidence = ev.Confidence
                    };
                    var matchDecision = EnsureDecision(state, ev.ImportedId);
                    matchDecision.CurrentStatus = ImportedTransactionStatus.Matched;
                    matchDecision.ActiveMatchedEventId = ev.MatchedEventId;
                    matchDecision.ActiveMatchType = ev.MatchType;
                    matchDecision.ActiveConfidence = ev.Confidence;
                    matchDecision.ActiveAppliedEventId = null;
                    matchDecision.ActiveAppliedType = null;
                    break;
                }
                case nameof(BankTransactionIgnored):
                {
                    var ev = JsonSerializer.Deserialize<BankTransactionIgnored>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.IgnoredIds.Add(ev.ImportedId);
                    var ignoreDecision = EnsureDecision(state, ev.ImportedId);
                    ignoreDecision.CurrentStatus = ImportedTransactionStatus.Ignored;
                    ignoreDecision.ActiveAppliedEventId = null;
                    ignoreDecision.ActiveAppliedType = null;
                    ignoreDecision.ActiveMatchedEventId = null;
                    ignoreDecision.ActiveMatchType = null;
                    ignoreDecision.ActiveConfidence = 0;
                    break;
                }
                case nameof(BankTransactionApplied):
                {
                    var ev = JsonSerializer.Deserialize<BankTransactionApplied>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.AppliedLinks[ev.ImportedId] = new AppliedLink
                    {
                        ImportedId = ev.ImportedId,
                        AppliedEventId = ev.AppliedEventId,
                        AppliedType = ev.AppliedType
                    };
                    var applyDecision = EnsureDecision(state, ev.ImportedId);
                    applyDecision.CurrentStatus = ImportedTransactionStatus.Applied;
                    applyDecision.ActiveAppliedEventId = ev.AppliedEventId;
                    applyDecision.ActiveAppliedType = ev.AppliedType;
                    applyDecision.ActiveMatchedEventId = null;
                    applyDecision.ActiveMatchType = null;
                    applyDecision.ActiveConfidence = 0;
                    break;
                }
                case nameof(BankTransactionDecisionReverted):
                {
                    var ev = JsonSerializer.Deserialize<BankTransactionDecisionReverted>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    var revertDecision = EnsureDecision(state, ev.ImportedId);
                    revertDecision.CurrentStatus = ImportedTransactionStatus.Unresolved;
                    revertDecision.ActiveAppliedEventId = null;
                    revertDecision.ActiveAppliedType = null;
                    revertDecision.ActiveMatchedEventId = null;
                    revertDecision.ActiveMatchType = null;
                    revertDecision.ActiveConfidence = 0;
                    // Update legacy collections
                    state.AppliedLinks.Remove(ev.ImportedId);
                    state.MatchedLinks.Remove(ev.ImportedId);
                    state.IgnoredIds.Remove(ev.ImportedId);
                    break;
                }
                case nameof(BankTransactionDecisionCorrected):
                {
                    var ev = JsonSerializer.Deserialize<BankTransactionDecisionCorrected>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    // Correction resets old decision (legacy collections cleaned in subsequent apply/match/ignore events)
                    var corrDecision = EnsureDecision(state, ev.ImportedId);
                    corrDecision.CurrentStatus = ImportedTransactionStatus.Unresolved;
                    corrDecision.ActiveAppliedEventId = null;
                    corrDecision.ActiveAppliedType = null;
                    corrDecision.ActiveMatchedEventId = null;
                    corrDecision.ActiveMatchType = null;
                    corrDecision.ActiveConfidence = 0;
                    state.AppliedLinks.Remove(ev.ImportedId);
                    state.MatchedLinks.Remove(ev.ImportedId);
                    state.IgnoredIds.Remove(ev.ImportedId);
                    break;
                }
                case nameof(BankImportBatchUndoRequested):
                case nameof(BankImportBatchUndoCompleted):
                    // Informational events; actual reverts are individual BankTransactionDecisionReverted events
                    break;
            }
        }

        return state;
    }

    private static ImportedTransactionDecision EnsureDecision(BankImportState state, Guid importedId)
    {
        if (!state.Decisions.TryGetValue(importedId, out var decision))
        {
            decision = new ImportedTransactionDecision { ImportedId = importedId };
            state.Decisions[importedId] = decision;
        }
        return decision;
    }
}
