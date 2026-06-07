using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ImportRules;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// ????????????????????????????????????????????
// Well-known stream
// ????????????????????????????????????????????

public static class ImportRuleStreams
{
    public static readonly StreamId RulePackStream = new(Guid.Parse("A1B2C3D4-0038-0038-0038-000000000001"));
}

// ????????????????????????????????????????????
// Commands
// ????????????????????????????????????????????

public sealed record CreateImportRulePackCommand(string Name, string Description, bool IsEnabled, DateOnly EffectiveDate);
public sealed record ModifyImportRulePackCommand(Guid PackId, string Name, string Description, bool IsEnabled, DateOnly EffectiveDate);
public sealed record ArchiveImportRulePackCommand(Guid PackId, string Reason, DateOnly EffectiveDate);
public sealed record DefineImportRuleCommand(
    Guid PackId, Guid? RuleId, int Version, string RuleKind,
    string MatchSpecJson, string ActionSpecJson, int Priority, bool IsEnabled, DateOnly EffectiveDate);
public sealed record ArchiveImportRuleCommand(Guid PackId, Guid RuleId, string Reason, DateOnly EffectiveDate);

// ????????????????????????????????????????????
// DTOs
// ????????????????????????????????????????????

public sealed record ImportRulePackDto(Guid PackId, string Name, string Description, bool IsEnabled, bool IsArchived);
public sealed record ImportRuleDto(
    Guid PackId, Guid RuleId, int Version, string Kind, int Priority,
    bool IsEnabled, bool IsArchived, string MatchSpecJson, string ActionSpecJson, DateOnly CreatedDate);
public sealed record ImportRulePackDetailDto(ImportRulePackDto Pack, List<ImportRuleDto> Rules);

// ????????????????????????????????????????????
// CRUD Handlers
// ????????????????????????????????????????????

public sealed class CreateImportRulePackHandler
{
    private readonly IEventStore _store;
    public CreateImportRulePackHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(CreateImportRulePackCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var packId = Guid.NewGuid();
        var ev = new ImportRulePackCreated(packId, cmd.Name, cmd.Description, cmd.IsEnabled, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportRuleStreams.RulePackStream,
            nameof(ImportRulePackCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
        return packId;
    }
}

public sealed class ModifyImportRulePackHandler
{
    private readonly IEventStore _store;
    public ModifyImportRulePackHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ModifyImportRulePackCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new ImportRulePackModified(cmd.PackId, cmd.Name, cmd.Description, cmd.IsEnabled, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportRuleStreams.RulePackStream,
            nameof(ImportRulePackModified), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class ArchiveImportRulePackHandler
{
    private readonly IEventStore _store;
    public ArchiveImportRulePackHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveImportRulePackCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new ImportRulePackArchived(cmd.PackId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportRuleStreams.RulePackStream,
            nameof(ImportRulePackArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class DefineImportRuleHandler
{
    private readonly IEventStore _store;
    public DefineImportRuleHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(DefineImportRuleCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ruleId = cmd.RuleId ?? Guid.NewGuid();
        var ev = new ImportRuleDefined(cmd.PackId, ruleId, cmd.Version, cmd.RuleKind,
            cmd.MatchSpecJson, cmd.ActionSpecJson, cmd.Priority, cmd.IsEnabled, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportRuleStreams.RulePackStream,
            nameof(ImportRuleDefined), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
        return ruleId;
    }
}

public sealed class ArchiveImportRuleHandler
{
    private readonly IEventStore _store;
    public ArchiveImportRuleHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveImportRuleCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new ImportRuleArchived(cmd.PackId, cmd.RuleId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportRuleStreams.RulePackStream,
            nameof(ImportRuleArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class GetImportRulePacksListHandler
{
    private readonly IEventStore _store;
    public GetImportRulePacksListHandler(IEventStore store) => _store = store;

    public async Task<List<ImportRulePackDto>> HandleAsync(bool includeArchived, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = ImportRulesProjector.Project(all);
        return state.Packs.Values
            .Where(p => includeArchived || !p.IsArchived)
            .Select(p => new ImportRulePackDto(p.PackId, p.Name, p.Description, p.IsEnabled, p.IsArchived))
            .ToList();
    }
}

public sealed class GetImportRulePackDetailHandler
{
    private readonly IEventStore _store;
    public GetImportRulePackDetailHandler(IEventStore store) => _store = store;

    public async Task<ImportRulePackDetailDto?> HandleAsync(Guid packId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = ImportRulesProjector.Project(all);

        if (!state.Packs.TryGetValue(packId, out var pack))
            return null;

        var rules = state.RulesByPack.GetValueOrDefault(packId, new List<ImportRuleRecord>())
            .Select(r => new ImportRuleDto(r.PackId, r.RuleId, r.Version, r.Kind, r.Priority,
                r.IsEnabled, r.IsArchived, r.MatchSpecJson, r.ActionSpecJson, r.CreatedDate))
            .ToList();

        return new ImportRulePackDetailDto(
            new ImportRulePackDto(pack.PackId, pack.Name, pack.Description, pack.IsEnabled, pack.IsArchived),
            rules);
    }
}

public sealed class PreviewRuleAgainstBatchHandler
{
    private readonly IEventStore _store;
    public PreviewRuleAgainstBatchHandler(IEventStore store) => _store = store;

    public async Task<List<ImportSuggestion>> HandleAsync(Guid batchId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var importState = BankImportProjector.Project(all);
        var rulesState = ImportRulesProjector.Project(all);
        var billingState = BillingProjector.Project(all);
        var categoryState = CategoryProjector.Project(all);

        var activeRules = rulesState.GetActiveRulesFlattened();
        var batchTxns = importState.ImportedTransactions.Values
            .Where(t => t.BatchId == batchId)
            .OrderBy(t => t.ImportedId);

        var results = new List<ImportSuggestion>();
        foreach (var txn in batchTxns)
        {
            var suggestions = ImportRuleEngine.Evaluate(txn, activeRules, billingState, null, categoryState);
            results.AddRange(suggestions);
        }

        return results;
    }
}
