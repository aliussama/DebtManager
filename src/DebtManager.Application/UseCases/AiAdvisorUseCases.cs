using System.Text.Json;
using DebtManager.Application.Ai;
using DebtManager.Application.Projections;
using DebtManager.Domain.Ai;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Well-known stream ---
internal static class AiStreams
{
    public static readonly StreamId AiStream = new(Guid.Parse("A1A10000-0039-0001-0001-000000000001"));
}

// --- Commands ---
public sealed record RunAiAnalysisCommand(DateOnly AsOfDate);
public sealed record ApproveAiProposalCommand(Guid ProposalId, DateOnly EffectiveDate);
public sealed record RejectAiProposalCommand(Guid ProposalId, string Reason, DateOnly EffectiveDate);
public sealed record UpdateAiSettingsCommand(bool Enabled, bool AllowInternetAccess, bool AllowAutoProposalGeneration, DateOnly EffectiveDate);

// --- DTOs ---
public sealed record AiInsightDto(Guid InsightId, string InsightCode, string Severity, string Area, string Title, string Message, DateOnly RecordedDate);
public sealed record AiProposalDto(Guid ProposalId, string ProposalKind, string ProposalJson, string Reason, string RiskLevel, string Status, string? RejectionReason, DateOnly CreatedDate);
public sealed record AiSettingsDto(bool Enabled, bool AllowInternetAccess, bool AllowAutoProposalGeneration);
public sealed record AiDashboardDto(AiSettingsDto Settings, IReadOnlyList<AiInsightDto> Insights, IReadOnlyList<AiProposalDto> Proposals);

// --- Handlers ---

public sealed class GetAiDashboardHandler
{
    private readonly IEventStore _store;
    public GetAiDashboardHandler(IEventStore store) => _store = store;

    public async Task<AiDashboardDto> HandleAsync(CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = AiAdvisorProjector.Project(all);

        var settings = new AiSettingsDto(state.Settings.Enabled, state.Settings.AllowInternetAccess, state.Settings.AllowAutoProposalGeneration);
        var insights = state.Insights.Values
            .OrderByDescending(i => i.RecordedDate)
            .Select(i => new AiInsightDto(i.InsightId, i.InsightCode, i.Severity, i.Area, i.Title, i.Message, i.RecordedDate))
            .ToList();
        var proposals = state.Proposals.Values
            .OrderByDescending(p => p.CreatedDate)
            .Select(p => new AiProposalDto(p.ProposalId, p.ProposalKind, p.ProposalJson, p.Reason, p.RiskLevel, p.Status.ToString(), p.RejectionReason, p.CreatedDate))
            .ToList();

        return new AiDashboardDto(settings, insights, proposals);
    }
}

public sealed class RunAiAnalysisHandler
{
    private readonly IEventStore _store;
    private readonly ProjectionRunner? _runner;

    public RunAiAnalysisHandler(IEventStore store, ProjectionRunner? runner = null)
    {
        _store = store;
        _runner = runner;
    }

    public async Task<(int InsightCount, int ProposalCount)> HandleAsync(
        RunAiAnalysisCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct, Identity.IdentityContext? identityContext = null)
    {
        identityContext?.Require(Domain.Identity.VaultPermission.PERM_RUN_AI_ANALYSIS);

        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);

        // Check settings — AI must be enabled
        var aiState = AiAdvisorProjector.Project(all);
        if (!aiState.Settings.Enabled)
            return (0, 0);

        // Project all states
        CashLedgerState ledgerState;
        BudgetState budgetState;
        CategoryState categoryState;

        if (_runner != null)
        {
            ledgerState = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
                e => CashLedgerProjector.Project(e), ct: ct);
            categoryState = await _runner.RunAsync(
                "CategoryState", e => CategoryProjector.Project(e), ct: ct);
            budgetState = await _runner.RunAsync(
                "BudgetState", e => BudgetProjector.Project(e), ct: ct);
        }
        else
        {
            ledgerState = CashLedgerProjector.Project(all);
            categoryState = CategoryProjector.Project(all);
            budgetState = BudgetProjector.Project(all);
        }

        var billingState = BillingProjector.Project(all, cmd.AsOfDate);
        var goalsState = GoalsProjector.Project(all);
        var retirementState = RetirementProjector.Project(all);
        var portfolioState = PortfolioProjector.Project(all);
        var assetsState = AssetsProjector.Project(all);
        var dqState = DataQualityProjector.Project(all);

        var input = new AiAdvisorEngine.AnalysisInput(
            ledgerState, budgetState, categoryState, billingState,
            goalsState, retirementState, portfolioState, assetsState,
            dqState, null, cmd.AsOfDate);

        var output = AiAdvisorEngine.Analyze(input);

        // Append events for new insights (skip duplicates)
        var existingInsightIds = new HashSet<Guid>(aiState.Insights.Keys);
        var existingProposalIds = new HashSet<Guid>(aiState.Proposals.Keys);
        var opt = DomainJson.Options;
        var correlationId = Guid.NewGuid();
        int insightCount = 0, proposalCount = 0;

        foreach (var insight in output.Insights)
        {
            if (existingInsightIds.Contains(insight.InsightId)) continue;

            var ev = new AiInsightRecorded(insight.InsightId, insight.InsightCode, insight.Severity,
                insight.Area, insight.Title, insight.Message, cmd.AsOfDate);
            await _store.AppendAsync(new EventEnvelope(
                new EventId(Guid.NewGuid()), AiStreams.AiStream,
                nameof(AiInsightRecorded), DateTimeOffset.UtcNow, cmd.AsOfDate,
                actorUserId, deviceId, correlationId, null, 1,
                JsonSerializer.Serialize(ev, opt)), ct);
            insightCount++;
        }

        foreach (var proposal in output.Proposals)
        {
            if (existingProposalIds.Contains(proposal.ProposalId)) continue;

            var ev = new AiProposalCreated(proposal.ProposalId, proposal.ProposalKind,
                proposal.ProposalJson, proposal.Reason, proposal.RiskLevel, cmd.AsOfDate);
            await _store.AppendAsync(new EventEnvelope(
                new EventId(Guid.NewGuid()), AiStreams.AiStream,
                nameof(AiProposalCreated), DateTimeOffset.UtcNow, cmd.AsOfDate,
                actorUserId, deviceId, correlationId, null, 1,
                JsonSerializer.Serialize(ev, opt)), ct);
            proposalCount++;
        }

        return (insightCount, proposalCount);
    }
}

public sealed class ApproveAiProposalHandler
{
    private readonly IEventStore _store;

    public ApproveAiProposalHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ApproveAiProposalCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct, Identity.IdentityContext? identityContext = null)
    {
        identityContext?.Require(Domain.Identity.VaultPermission.PERM_APPROVE_AI_PROPOSALS);

        // Verify proposal exists and is Pending
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = AiAdvisorProjector.Project(all);
        if (!state.Proposals.TryGetValue(cmd.ProposalId, out var proposal))
            throw new InvalidOperationException($"Proposal {cmd.ProposalId} not found.");
        if (proposal.Status != AiProposalStatus.Pending)
            return; // idempotent

        var ev = new AiProposalApproved(cmd.ProposalId, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), AiStreams.AiStream,
            nameof(AiProposalApproved), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class RejectAiProposalHandler
{
    private readonly IEventStore _store;
    public RejectAiProposalHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(RejectAiProposalCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = AiAdvisorProjector.Project(all);
        if (!state.Proposals.TryGetValue(cmd.ProposalId, out var proposal))
            throw new InvalidOperationException($"Proposal {cmd.ProposalId} not found.");
        if (proposal.Status != AiProposalStatus.Pending)
            return; // idempotent

        var ev = new AiProposalRejected(cmd.ProposalId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), AiStreams.AiStream,
            nameof(AiProposalRejected), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class UpdateAiSettingsHandler
{
    private readonly IEventStore _store;
    public UpdateAiSettingsHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(UpdateAiSettingsCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new AiSettingsUpdated(cmd.Enabled, cmd.AllowInternetAccess, cmd.AllowAutoProposalGeneration, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), AiStreams.AiStream,
            nameof(AiSettingsUpdated), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class GetAiSettingsHandler
{
    private readonly IEventStore _store;
    public GetAiSettingsHandler(IEventStore store) => _store = store;

    public async Task<AiSettingsDto> HandleAsync(CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = AiAdvisorProjector.Project(all);
        return new AiSettingsDto(state.Settings.Enabled, state.Settings.AllowInternetAccess, state.Settings.AllowAutoProposalGeneration);
    }
}
