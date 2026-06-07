using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Quality;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// ?? Query / Command records ??????????????????????????????????????????????

public sealed record DataQualityDashboardQuery;

public sealed record DataQualityDashboardResult(
    DataQualityScanSummary? LatestScan,
    List<DataQualityIssue> ActiveIssues,
    int CriticalCount,
    int ErrorCount,
    int WarningCount,
    int InfoCount,
    DateTimeOffset? LastScanTime);

public sealed record RunDataQualityScanCommand;

public sealed record DataQualityIssuesQuery(
    DataQualitySeverity? Severity = null,
    DataQualityArea? Area = null,
    string? SearchText = null,
    bool OnlyUnresolved = false);

public sealed record AcknowledgeIssueCommand(Guid IssueId, string Note);
public sealed record ResolveIssueCommand(Guid IssueId, string ResolutionKind, string ResolutionDetailsJson);
public sealed record PreviewFixCommand(Guid IssueId, string FixKind);
public sealed record PreviewFixResult(string PreviewDescription, string EventsSummary, bool CanApply);
public sealed record ApplyFixCommand(Guid IssueId, string FixKind);

// ?? Handlers ?????????????????????????????????????????????????????????????

public sealed class RunDataQualityScanHandler
{
    private readonly IEventStore _store;

    public RunDataQualityScanHandler(IEventStore store) => _store = store;

    public async Task<DataQualityScanSummary> HandleAsync(
        Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var scanId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var asOfDate = DateOnly.FromDateTime(DateTime.Today);

        var allEnvelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);

        var cashState = CashLedgerProjector.Project(allEnvelopes);
        var bankState = BankImportProjector.Project(allEnvelopes);
        var recurringState = RecurringProjector.Project(allEnvelopes);
        var budgetState = BudgetProjector.Project(allEnvelopes);
        var categoryState = CategoryProjector.Project(allEnvelopes);
        var assetsState = AssetsProjector.Project(allEnvelopes);
        var portfolioState = PortfolioProjector.Project(allEnvelopes);
        var taxState = TaxProjector.Project(allEnvelopes);
        var goalsState = GoalsProjector.Project(allEnvelopes);
        var setupState = SetupProjector.Project(allEnvelopes);

        var issues = DataQualityRules.RunAll(
            allEnvelopes, cashState, bankState, recurringState,
            budgetState, categoryState, assetsState, portfolioState,
            taxState, goalsState, setupState, asOfDate);

        // Build summary
        var completedAt = DateTimeOffset.UtcNow;
        var summary = new DataQualityScanSummary
        {
            ScanId = scanId,
            TotalIssues = issues.Count,
            CountsBySeverity = issues.GroupBy(i => i.Severity)
                .ToDictionary(g => g.Key, g => g.Count()),
            CountsByArea = issues.GroupBy(i => i.Area)
                .ToDictionary(g => g.Key, g => g.Count()),
            TopIssues = issues.OrderByDescending(i => i.Severity).Take(10).ToList(),
            GeneratedAt = completedAt
        };

        // Append scan event
        var ev = new DataQualityScanRecorded(
            EffectiveDate: asOfDate,
            ScanId: scanId,
            StartedAt: startedAt,
            CompletedAt: completedAt,
            AppVersion: "1.0.0",
            RuleSetVersion: DataQualityRules.RuleSetVersion,
            SummaryJson: JsonSerializer.Serialize(summary, DomainJson.Options));

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(scanId),
            nameof(DataQualityScanRecorded),
            DateTimeOffset.UtcNow,
            asOfDate,
            actorUserId,
            deviceId,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options));

        await _store.AppendAsync(envelope, ct);

        return summary;
    }
}

public sealed class GetDataQualityDashboardHandler
{
    private readonly IEventStore _store;

    public GetDataQualityDashboardHandler(IEventStore store) => _store = store;

    public async Task<DataQualityDashboardResult> HandleAsync(CancellationToken ct)
    {
        var asOfDate = DateOnly.FromDateTime(DateTime.Today);
        var allEnvelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);

        var dqState = DataQualityProjector.Project(allEnvelopes);

        var cashState = CashLedgerProjector.Project(allEnvelopes);
        var bankState = BankImportProjector.Project(allEnvelopes);
        var recurringState = RecurringProjector.Project(allEnvelopes);
        var budgetState = BudgetProjector.Project(allEnvelopes);
        var categoryState = CategoryProjector.Project(allEnvelopes);
        var assetsState = AssetsProjector.Project(allEnvelopes);
        var portfolioState = PortfolioProjector.Project(allEnvelopes);
        var taxState = TaxProjector.Project(allEnvelopes);
        var goalsState = GoalsProjector.Project(allEnvelopes);
        var setupState = SetupProjector.Project(allEnvelopes);

        var issues = DataQualityRules.RunAll(
            allEnvelopes, cashState, bankState, recurringState,
            budgetState, categoryState, assetsState, portfolioState,
            taxState, goalsState, setupState, asOfDate);

        // Filter out resolved issues
        var activeIssues = issues
            .Where(i => !dqState.ResolvedIssueIds.Contains(i.IssueId))
            .ToList();

        // Latest scan
        var latestScan = dqState.Scans.Values
            .OrderByDescending(s => s.CompletedAt)
            .FirstOrDefault();

        DataQualityScanSummary? latestSummary = null;
        if (latestScan != null)
        {
            try
            {
                latestSummary = JsonSerializer.Deserialize<DataQualityScanSummary>(
                    latestScan.SummaryJson, DomainJson.Options);
            }
            catch { /* ignore deserialization failures */ }
        }

        return new DataQualityDashboardResult(
            LatestScan: latestSummary,
            ActiveIssues: activeIssues,
            CriticalCount: activeIssues.Count(i => i.Severity == DataQualitySeverity.Critical),
            ErrorCount: activeIssues.Count(i => i.Severity == DataQualitySeverity.Error),
            WarningCount: activeIssues.Count(i => i.Severity == DataQualitySeverity.Warning),
            InfoCount: activeIssues.Count(i => i.Severity == DataQualitySeverity.Info),
            LastScanTime: latestScan?.CompletedAt);
    }
}

public sealed class GetDataQualityIssuesHandler
{
    private readonly IEventStore _store;

    public GetDataQualityIssuesHandler(IEventStore store) => _store = store;

    public async Task<List<DataQualityIssue>> HandleAsync(DataQualityIssuesQuery query, CancellationToken ct)
    {
        var asOfDate = DateOnly.FromDateTime(DateTime.Today);
        var allEnvelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);

        var dqState = DataQualityProjector.Project(allEnvelopes);

        var cashState = CashLedgerProjector.Project(allEnvelopes);
        var bankState = BankImportProjector.Project(allEnvelopes);
        var recurringState = RecurringProjector.Project(allEnvelopes);
        var budgetState = BudgetProjector.Project(allEnvelopes);
        var categoryState = CategoryProjector.Project(allEnvelopes);
        var assetsState = AssetsProjector.Project(allEnvelopes);
        var portfolioState = PortfolioProjector.Project(allEnvelopes);
        var taxState = TaxProjector.Project(allEnvelopes);
        var goalsState = GoalsProjector.Project(allEnvelopes);
        var setupState = SetupProjector.Project(allEnvelopes);

        var issues = DataQualityRules.RunAll(
            allEnvelopes, cashState, bankState, recurringState,
            budgetState, categoryState, assetsState, portfolioState,
            taxState, goalsState, setupState, asOfDate);

        IEnumerable<DataQualityIssue> filtered = issues;

        if (query.OnlyUnresolved)
            filtered = filtered.Where(i => !dqState.ResolvedIssueIds.Contains(i.IssueId));

        if (query.Severity.HasValue)
            filtered = filtered.Where(i => i.Severity == query.Severity.Value);

        if (query.Area.HasValue)
            filtered = filtered.Where(i => i.Area == query.Area.Value);

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var search = query.SearchText.Trim();
            filtered = filtered.Where(i =>
                i.Code.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                i.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                i.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.OrderByDescending(i => i.Severity).ThenBy(i => i.Code).ToList();
    }
}

public sealed class AcknowledgeIssueHandler
{
    private readonly IEventStore _store;

    public AcknowledgeIssueHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(
        AcknowledgeIssueCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new DataQualityIssueAcknowledged(
            EffectiveDate: DateOnly.FromDateTime(DateTime.Today),
            IssueId: cmd.IssueId,
            Note: cmd.Note);

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(cmd.IssueId),
            nameof(DataQualityIssueAcknowledged),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            actorUserId,
            deviceId,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options));

        await _store.AppendAsync(envelope, ct);
    }
}

public sealed class ResolveIssueHandler
{
    private readonly IEventStore _store;

    public ResolveIssueHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(
        ResolveIssueCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new DataQualityIssueResolved(
            EffectiveDate: DateOnly.FromDateTime(DateTime.Today),
            IssueId: cmd.IssueId,
            ResolutionKind: cmd.ResolutionKind,
            ResolutionDetailsJson: cmd.ResolutionDetailsJson);

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(cmd.IssueId),
            nameof(DataQualityIssueResolved),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            actorUserId,
            deviceId,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options));

        await _store.AppendAsync(envelope, ct);
    }
}

public sealed class PreviewFixHandler
{
    private readonly IEventStore _store;

    public PreviewFixHandler(IEventStore store) => _store = store;

    public async Task<PreviewFixResult> HandleAsync(PreviewFixCommand cmd, CancellationToken ct)
    {
        var asOfDate = DateOnly.FromDateTime(DateTime.Today);
        var allEnvelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);

        var cashState = CashLedgerProjector.Project(allEnvelopes);
        var bankState = BankImportProjector.Project(allEnvelopes);
        var recurringState = RecurringProjector.Project(allEnvelopes);
        var budgetState = BudgetProjector.Project(allEnvelopes);
        var categoryState = CategoryProjector.Project(allEnvelopes);
        var assetsState = AssetsProjector.Project(allEnvelopes);
        var portfolioState = PortfolioProjector.Project(allEnvelopes);
        var taxState = TaxProjector.Project(allEnvelopes);
        var goalsState = GoalsProjector.Project(allEnvelopes);
        var setupState = SetupProjector.Project(allEnvelopes);

        var issues = DataQualityRules.RunAll(
            allEnvelopes, cashState, bankState, recurringState,
            budgetState, categoryState, assetsState, portfolioState,
            taxState, goalsState, setupState, asOfDate);

        var issue = issues.FirstOrDefault(i => i.IssueId == cmd.IssueId);
        if (issue == null)
            return new PreviewFixResult("Issue not found or already resolved.", "", false);

        var fix = issue.SuggestedFixes.FirstOrDefault(f => f.FixKind == cmd.FixKind);
        if (fix == null)
            return new PreviewFixResult($"Fix kind '{cmd.FixKind}' not available for this issue.", "", false);

        if (fix.RequiresUserInput)
            return new PreviewFixResult(fix.Description, "This fix requires additional user input.", false);

        return cmd.FixKind switch
        {
            "RevertBankDecision" => new PreviewFixResult(
                $"Will append a BankTransactionDecisionReverted event for imported transaction.",
                "Events: BankTransactionDecisionReverted + DataQualityAutoFixApplied",
                true),
            "ArchiveRecurring" => new PreviewFixResult(
                $"Will archive the recurring template that references an archived account/category.",
                "Events: RecurringTransactionArchived + DataQualityAutoFixApplied",
                true),
            _ => new PreviewFixResult(fix.Description, "Preview not available for this fix type.", false)
        };
    }
}

public sealed class ApplyFixHandler
{
    private readonly IEventStore _store;
    private readonly RevertImportedDecisionHandler? _revertHandler;
    private readonly ArchiveRecurringHandler? _archiveRecurringHandler;

    public ApplyFixHandler(
        IEventStore store,
        RevertImportedDecisionHandler? revertHandler = null,
        ArchiveRecurringHandler? archiveRecurringHandler = null)
    {
        _store = store;
        _revertHandler = revertHandler;
        _archiveRecurringHandler = archiveRecurringHandler;
    }

    public async Task HandleAsync(
        ApplyFixCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct, Identity.IdentityContext? identityContext = null)
    {
        identityContext?.Require(Domain.Identity.VaultPermission.PERM_APPLY_DATA_FIXES);

        // Idempotency: check if fix already applied
        var allEnvelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var dqState = DataQualityProjector.Project(allEnvelopes);

        if (dqState.AppliedFixesByIssue.TryGetValue(cmd.IssueId, out var existingFixes) && existingFixes.Count > 0)
            return; // Already applied — idempotent

        var appliedEventIds = new List<Guid>();
        var asOfDate = DateOnly.FromDateTime(DateTime.Today);

        switch (cmd.FixKind)
        {
            case "RevertBankDecision":
            {
                if (_revertHandler == null)
                    throw new InvalidOperationException("RevertImportedDecisionHandler not available.");

                var importedId = cmd.IssueId; // For DQ-BANK-002, IssueId is derived from importedId
                // Find the actual importedId from the issue
                var bankState = BankImportProjector.Project(allEnvelopes);
                var candidateId = FindImportedIdFromBankIssue(cmd.IssueId, bankState);
                if (candidateId.HasValue)
                {
                    await _revertHandler.HandleAsync(
                        new RevertImportedDecisionCommand(candidateId.Value, DateOnly.FromDateTime(DateTime.Today), "Auto-fix: resolve decision conflict"),
                        actorUserId, deviceId, ct);
                    appliedEventIds.Add(candidateId.Value);
                }
                break;
            }
            case "ArchiveRecurring":
            {
                if (_archiveRecurringHandler == null)
                    throw new InvalidOperationException("ArchiveRecurringHandler not available.");

                var recurringState = RecurringProjector.Project(allEnvelopes);
                var recurringId = FindRecurringIdFromIssue(cmd.IssueId, recurringState);
                if (recurringId.HasValue)
                {
                    await _archiveRecurringHandler.HandleAsync(
                        new ArchiveRecurringCommand(recurringId.Value, "Auto-fix: archived reference detected"),
                        actorUserId, deviceId, ct);
                    appliedEventIds.Add(recurringId.Value);
                }
                break;
            }
            default:
                throw new NotSupportedException($"Fix kind '{cmd.FixKind}' cannot be auto-applied.");
        }

        // Append DataQualityAutoFixApplied
        var fixId = Guid.NewGuid();
        var fixEv = new DataQualityAutoFixApplied(
            EffectiveDate: asOfDate,
            FixId: fixId,
            IssueId: cmd.IssueId,
            FixKind: cmd.FixKind,
            AppliedEventIds: appliedEventIds.ToArray(),
            Notes: $"Auto-fix applied: {cmd.FixKind}");

        var fixEnvelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(cmd.IssueId),
            nameof(DataQualityAutoFixApplied),
            DateTimeOffset.UtcNow,
            asOfDate,
            actorUserId,
            deviceId,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(fixEv, DomainJson.Options));

        await _store.AppendAsync(fixEnvelope, ct);

        // Also resolve the issue
        var resolveEv = new DataQualityIssueResolved(
            EffectiveDate: asOfDate,
            IssueId: cmd.IssueId,
            ResolutionKind: "AutoFix",
            ResolutionDetailsJson: JsonSerializer.Serialize(new { cmd.FixKind, fixId }, DomainJson.Options));

        var resolveEnvelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(cmd.IssueId),
            nameof(DataQualityIssueResolved),
            DateTimeOffset.UtcNow,
            asOfDate,
            actorUserId,
            deviceId,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(resolveEv, DomainJson.Options));

        await _store.AppendAsync(resolveEnvelope, ct);
    }

    private static Guid? FindImportedIdFromBankIssue(Guid issueId, BankImportState bankState)
    {
        foreach (var importedId in bankState.Decisions.Keys)
        {
            var expected = DataQualityRules.DeterministicId("DQ-BANK-002", importedId.ToString());
            if (expected == issueId) return importedId;
        }
        return null;
    }

    private static Guid? FindRecurringIdFromIssue(Guid issueId, RecurringState recurringState)
    {
        foreach (var recurringId in recurringState.Items.Keys)
        {
            var expected1 = DataQualityRules.DeterministicId("DQ-REC-002", $"{recurringId}|account");
            var expected2 = DataQualityRules.DeterministicId("DQ-REC-002", $"{recurringId}|category");
            if (expected1 == issueId || expected2 == issueId) return recurringId;
        }
        return null;
    }
}
