using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DebtManager.Application.Projections;
using DebtManager.Domain.Events;
using DebtManager.Domain.Import;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// ========== DTOs ==========

public sealed record ImportProfileDto(Guid ProfileId, string Name, string MappingJson, bool IsArchived);

public sealed record ImportPreviewRowDto(
    DateOnly TxnDate, decimal Amount, string CurrencyCode,
    string Description, string Reference, string Counterparty, string Direction);

public sealed record ImportPreviewResultDto(
    IReadOnlyList<ImportPreviewRowDto> Rows,
    int TotalRows, int DuplicateRows, bool IsDuplicateBatch);

public sealed record ReconciliationRowDto(
Guid ImportedId, DateOnly TxnDate, decimal Amount, string CurrencyCode,
string Description, string Direction, string Status,
Guid? MatchedEventId, string? MatchType, decimal Confidence,
string? SuggestedCategory = null, Guid? SuggestedAccountId = null,
string? SuggestedActionKind = null, int? SuggestedConfidence = null,
string? SuggestedReason = null);

// ========== Commands ==========

public sealed record CreateBankImportProfileCommand(string Name, string MappingJson, DateOnly EffectiveDate);
public sealed record ModifyBankImportProfileCommand(Guid ProfileId, string MappingJson, DateOnly EffectiveDate, string Reason);
public sealed record ArchiveBankImportProfileCommand(Guid ProfileId, DateOnly EffectiveDate, string Reason);

public sealed record StartBankImportBatchCommand(
    Guid ProfileId, Guid AccountId, string FileName, string CsvContent, DateOnly EffectiveDate);

public sealed record PreviewBankImportCommand(Guid ProfileId, Guid AccountId, string CsvContent);

public sealed record ApplyImportedTransactionCommand(
    Guid ImportedId, string Mode, // "Income", "Expense", "Transfer"
    string? CategoryId, string? Notes, Guid? ToAccountId);

public sealed record ConfirmMatchImportedTransactionCommand(
    Guid ImportedId, Guid MatchedEventId, string? Notes);

public sealed record IgnoreImportedTransactionCommand(Guid ImportedId, string Reason);

// ========== Handlers ==========

// --- Well-known stream IDs for import events ---
public static class ImportStreams
{
    public static readonly StreamId ProfileStream = new(Guid.Parse("A1B2C3D4-0001-0001-0001-000000000001"));
    public static readonly StreamId ImportStream = new(Guid.Parse("A1B2C3D4-0002-0002-0002-000000000002"));
}

public sealed class CreateBankImportProfileHandler
{
    private readonly IEventStore _store;
    public CreateBankImportProfileHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(CreateBankImportProfileCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var profileId = Guid.NewGuid();
        var ev = new BankImportProfileCreated(profileId, cmd.Name, cmd.MappingJson, cmd.EffectiveDate);

        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportStreams.ProfileStream,
            nameof(BankImportProfileCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)
        ), ct);

        return profileId;
    }
}

public sealed class ModifyBankImportProfileHandler
{
    private readonly IEventStore _store;
    public ModifyBankImportProfileHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ModifyBankImportProfileCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new BankImportProfileModified(cmd.ProfileId, cmd.MappingJson, cmd.EffectiveDate, cmd.Reason);

        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportStreams.ProfileStream,
            nameof(BankImportProfileModified), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)
        ), ct);
    }
}

public sealed class ArchiveBankImportProfileHandler
{
    private readonly IEventStore _store;
    public ArchiveBankImportProfileHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveBankImportProfileCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new BankImportProfileArchived(cmd.ProfileId, cmd.EffectiveDate, cmd.Reason);

        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportStreams.ProfileStream,
            nameof(BankImportProfileArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)
        ), ct);
    }
}

public sealed class GetBankImportProfilesListHandler
{
    private readonly IEventStore _store;
    public GetBankImportProfilesListHandler(IEventStore store) => _store = store;

    public async Task<IReadOnlyList<ImportProfileDto>> HandleAsync(CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = BankImportProjector.Project(all);

        return state.Profiles.Values
            .Select(p => new ImportProfileDto(p.ProfileId, p.Name, p.MappingJson, p.IsArchived))
            .ToList();
    }
}

public sealed class PreviewBankImportHandler
{
    private readonly IEventStore _store;
    public PreviewBankImportHandler(IEventStore store) => _store = store;

    public async Task<ImportPreviewResultDto> HandleAsync(PreviewBankImportCommand cmd, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = BankImportProjector.Project(all);

        if (!state.Profiles.TryGetValue(cmd.ProfileId, out var profileItem))
            throw new InvalidOperationException("Profile not found");

        var profile = BankImportProfile.FromJson(profileItem.MappingJson);
        var rows = BankCsvParser.Parse(cmd.CsvContent, profile);

        var fileHash = ComputeSha256(cmd.CsvContent);
        var isDupBatch = state.IsDuplicateBatch(cmd.ProfileId, cmd.AccountId, fileHash);

        int dupes = 0;
        var previewRows = new List<ImportPreviewRowDto>();
        foreach (var r in rows)
        {
            if (state.IsDuplicateTransaction(cmd.AccountId, r.TxnDate, r.Amount, r.Description))
            {
                dupes++;
                continue;
            }
            previewRows.Add(new ImportPreviewRowDto(r.TxnDate, r.Amount, r.CurrencyCode,
                r.Description, r.Reference, r.Counterparty, r.Direction));
        }

        return new ImportPreviewResultDto(previewRows, rows.Count, dupes, isDupBatch);
    }

    internal static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed class StartBankImportBatchHandler
{
    private readonly IEventStore _store;
    public StartBankImportBatchHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(StartBankImportBatchCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = BankImportProjector.Project(all);

        if (!state.Profiles.TryGetValue(cmd.ProfileId, out var profileItem))
            throw new InvalidOperationException("Profile not found");

        var profile = BankImportProfile.FromJson(profileItem.MappingJson);
        var rows = BankCsvParser.Parse(cmd.CsvContent, profile);

        var fileHash = PreviewBankImportHandler.ComputeSha256(cmd.CsvContent);

        // Idempotency: if same file hash already completed for this profile+account, return existing batch
        if (state.IsDuplicateBatch(cmd.ProfileId, cmd.AccountId, fileHash))
        {
            var existing = state.Batches.Values.First(b =>
                b.ProfileId == cmd.ProfileId && b.AccountId == cmd.AccountId &&
                b.FileHashSha256 == fileHash && b.IsCompleted);
            return existing.BatchId;
        }

        var batchId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        // 1. Batch started
        var batchStarted = new BankImportBatchStarted(batchId, cmd.ProfileId, cmd.AccountId,
            cmd.FileName, fileHash, rows.Count, cmd.EffectiveDate);

        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportStreams.ImportStream,
            nameof(BankImportBatchStarted), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(batchStarted, DomainJson.Options)
        ), ct);

        // 2. Import each row (deduplicating)
        int imported = 0;
        int skipped = 0;
        var seenInBatch = new HashSet<string>();

        foreach (var row in rows)
        {
            var rowKey = $"{row.TxnDate}|{row.Amount}|{row.Description}";
            if (seenInBatch.Contains(rowKey))
            {
                skipped++;
                continue;
            }

            if (state.IsDuplicateTransaction(cmd.AccountId, row.TxnDate, row.Amount, row.Description))
            {
                skipped++;
                continue;
            }

            seenInBatch.Add(rowKey);
            var importedId = Guid.NewGuid();

            var txnEvent = new BankTransactionImported(
                importedId, batchId, cmd.AccountId, row.TxnDate, row.Amount,
                row.CurrencyCode, row.Description, row.Reference, row.Counterparty,
                row.Direction, row.RawLine, cmd.EffectiveDate);

            await _store.AppendAsync(new EventEnvelope(
                new EventId(Guid.NewGuid()), ImportStreams.ImportStream,
                nameof(BankTransactionImported), DateTimeOffset.UtcNow, cmd.EffectiveDate,
                actorUserId, deviceId, correlationId, null, 1,
                JsonSerializer.Serialize(txnEvent, DomainJson.Options)
            ), ct);

            imported++;
        }

        // 3. Batch completed
        var batchCompleted = new BankImportBatchCompleted(batchId, imported, skipped, cmd.EffectiveDate);

        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportStreams.ImportStream,
            nameof(BankImportBatchCompleted), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(batchCompleted, DomainJson.Options)
        ), ct);

        return batchId;
    }
}

public sealed class GetReconciliationCandidatesHandler
{
    private readonly IEventStore _store;
    private readonly ProjectionRunner? _runner;
    public GetReconciliationCandidatesHandler(IEventStore store, ProjectionRunner? runner = null)
    {
        _store = store;
        _runner = runner;
    }

    public async Task<IReadOnlyList<ReconciliationRowDto>> HandleAsync(Guid accountId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var importState = BankImportProjector.Project(all);

        CashLedgerState ledgerState;
        if (_runner != null)
        {
            ledgerState = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
                e => CashLedgerProjector.Project(e),
                ct: ct);
        }
        else
        {
            ledgerState = CashLedgerProjector.Project(all);
        }

        var candidates = ReconciliationEngine.Reconcile(importState, ledgerState, accountId);

        return candidates.Select(c =>
        {
            var imported = importState.ImportedTransactions.GetValueOrDefault(c.ImportedId);
            return new ReconciliationRowDto(
                c.ImportedId, c.TxnDate, c.Amount,
                imported?.CurrencyCode ?? "EGP",
                c.Description, c.Direction, c.Status,
                c.MatchedEventId, c.MatchType, c.Confidence);
        }).ToList();
    }
}

public sealed class ApplyImportedTransactionHandler
{
    private readonly IEventStore _store;
    public ApplyImportedTransactionHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ApplyImportedTransactionCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var importState = BankImportProjector.Project(all);

        if (!importState.ImportedTransactions.TryGetValue(cmd.ImportedId, out var imported))
            throw new InvalidOperationException("Imported transaction not found");

        // Idempotency: already applied
        if (importState.AppliedLinks.ContainsKey(cmd.ImportedId))
            throw new InvalidOperationException("Transaction already applied");
        if (importState.MatchedLinks.ContainsKey(cmd.ImportedId))
            throw new InvalidOperationException("Transaction already matched");
        if (importState.IgnoredIds.Contains(cmd.ImportedId))
            throw new InvalidOperationException("Transaction already ignored");

        var correlationId = Guid.NewGuid();
        var appliedEventId = Guid.NewGuid();
        var effectiveDate = imported.TxnDate;
        var currency = imported.CurrencyCode switch
        {
            "EGP" => Currency.EGP,
            "USD" => Currency.USD,
            _ => new Currency(imported.CurrencyCode, 2)
        };

        string appliedType;

        switch (cmd.Mode)
        {
            case "Income":
            {
                var income = new IncomeRecorded(imported.AccountId, new Money(imported.Amount, currency),
                    effectiveDate, cmd.Notes ?? imported.Description);

                await _store.AppendAsync(new EventEnvelope(
                    new EventId(appliedEventId), new StreamId(imported.AccountId),
                    nameof(IncomeRecorded), DateTimeOffset.UtcNow, effectiveDate,
                    actorUserId, deviceId, correlationId, null, 1,
                    JsonSerializer.Serialize(income, DomainJson.Options)
                ), ct);
                appliedType = "IncomeRecorded";
                break;
            }
            case "Expense":
            {
                var expense = new ExpenseRecorded(imported.AccountId, new Money(imported.Amount, currency),
                    effectiveDate, cmd.CategoryId ?? "Imported", cmd.Notes ?? imported.Description);

                await _store.AppendAsync(new EventEnvelope(
                    new EventId(appliedEventId), new StreamId(imported.AccountId),
                    nameof(ExpenseRecorded), DateTimeOffset.UtcNow, effectiveDate,
                    actorUserId, deviceId, correlationId, null, 1,
                    JsonSerializer.Serialize(expense, DomainJson.Options)
                ), ct);
                appliedType = "ExpenseRecorded";
                break;
            }
            case "Transfer":
            {
                if (!cmd.ToAccountId.HasValue)
                    throw new InvalidOperationException("Transfer requires ToAccountId");

                var transfer = new TransferRecorded(Guid.NewGuid(), imported.AccountId, cmd.ToAccountId.Value,
                    imported.Amount, imported.CurrencyCode, effectiveDate, cmd.Notes ?? imported.Description);

                await _store.AppendAsync(new EventEnvelope(
                    new EventId(appliedEventId), new StreamId(imported.AccountId),
                    nameof(TransferRecorded), DateTimeOffset.UtcNow, effectiveDate,
                    actorUserId, deviceId, correlationId, null, 1,
                    JsonSerializer.Serialize(transfer, DomainJson.Options)
                ), ct);
                appliedType = "TransferRecorded";
                break;
            }
            default:
                throw new InvalidOperationException($"Unknown apply mode: {cmd.Mode}");
        }

        // Record the applied link
        var appliedEvent = new BankTransactionApplied(cmd.ImportedId, appliedEventId, appliedType, effectiveDate);

        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportStreams.ImportStream,
            nameof(BankTransactionApplied), DateTimeOffset.UtcNow, effectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(appliedEvent, DomainJson.Options)
        ), ct);
    }
}

public sealed class ConfirmMatchImportedTransactionHandler
{
    private readonly IEventStore _store;
    public ConfirmMatchImportedTransactionHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ConfirmMatchImportedTransactionCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var importState = BankImportProjector.Project(all);

        if (!importState.ImportedTransactions.ContainsKey(cmd.ImportedId))
            throw new InvalidOperationException("Imported transaction not found");

        if (importState.MatchedLinks.ContainsKey(cmd.ImportedId))
            throw new InvalidOperationException("Transaction already matched");
        if (importState.AppliedLinks.ContainsKey(cmd.ImportedId))
            throw new InvalidOperationException("Transaction already applied");
        if (importState.IgnoredIds.Contains(cmd.ImportedId))
            throw new InvalidOperationException("Transaction already ignored");

        var effectiveDate = DateOnly.FromDateTime(DateTime.Today);
        var ev = new BankTransactionMatched(cmd.ImportedId, cmd.MatchedEventId, "Manual", 1.0m, effectiveDate, cmd.Notes);

        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportStreams.ImportStream,
            nameof(BankTransactionMatched), DateTimeOffset.UtcNow, effectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)
        ), ct);
    }
}

public sealed class IgnoreImportedTransactionHandler
{
    private readonly IEventStore _store;
    public IgnoreImportedTransactionHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(IgnoreImportedTransactionCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var importState = BankImportProjector.Project(all);

        if (!importState.ImportedTransactions.ContainsKey(cmd.ImportedId))
            throw new InvalidOperationException("Imported transaction not found");

        if (importState.IgnoredIds.Contains(cmd.ImportedId))
            throw new InvalidOperationException("Transaction already ignored");
        if (importState.AppliedLinks.ContainsKey(cmd.ImportedId))
            throw new InvalidOperationException("Transaction already applied");
        if (importState.MatchedLinks.ContainsKey(cmd.ImportedId))
            throw new InvalidOperationException("Transaction already matched");

        var effectiveDate = DateOnly.FromDateTime(DateTime.Today);
        var ev = new BankTransactionIgnored(cmd.ImportedId, effectiveDate, cmd.Reason);

        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportStreams.ImportStream,
            nameof(BankTransactionIgnored), DateTimeOffset.UtcNow, effectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)
        ), ct);
    }
}
