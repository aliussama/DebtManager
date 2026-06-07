using System.Text.Json;
using DebtManager.Application.Projections;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Commands ---

public sealed record DefineBudgetCommand(
    Guid? BudgetId, int PeriodYear, int PeriodMonth, string CurrencyCode,
    string ScopeType, Guid? CategoryId, Guid? AccountId,
    decimal LimitAmount, string CarryPolicy);

public sealed record AdjustBudgetCommand(Guid BudgetId, decimal NewLimitAmount, string Reason);
public sealed record ArchiveBudgetCommand(Guid BudgetId, string Reason);

// --- Query ---

public sealed record BudgetDashboardQuery(int Year, int Month, Guid? AccountId = null);

public sealed record BudgetDashboardDto(
    int Year, int Month,
    IReadOnlyList<BudgetUtilizationRow> Utilizations,
    decimal TotalLimit, decimal TotalActual, decimal TotalRemaining);

// --- Handlers ---

public sealed class DefineBudgetHandler
{
    private readonly IEventStore _store;
    public DefineBudgetHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(DefineBudgetCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var id = cmd.BudgetId ?? Guid.NewGuid();
        var ev = new BudgetDefined(id, cmd.PeriodYear, cmd.PeriodMonth, cmd.CurrencyCode,
            cmd.ScopeType, cmd.CategoryId, cmd.AccountId, cmd.LimitAmount, cmd.CarryPolicy,
            DateOnly.FromDateTime(DateTime.Today));
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(id),
            nameof(BudgetDefined), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return id;
    }
}

public sealed class AdjustBudgetHandler
{
    private readonly IEventStore _store;
    public AdjustBudgetHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(AdjustBudgetCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new BudgetAdjusted(cmd.BudgetId, cmd.NewLimitAmount, DateOnly.FromDateTime(DateTime.Today), cmd.Reason);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.BudgetId),
            nameof(BudgetAdjusted), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class ArchiveBudgetHandler
{
    private readonly IEventStore _store;
    public ArchiveBudgetHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveBudgetCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new BudgetArchived(cmd.BudgetId, DateOnly.FromDateTime(DateTime.Today), cmd.Reason);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.BudgetId),
            nameof(BudgetArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class GetBudgetDashboardHandler
{
    private readonly IEventStore _store;
    private readonly ProjectionRunner? _runner;
    public GetBudgetDashboardHandler(IEventStore store, ProjectionRunner? runner = null)
    {
        _store = store;
        _runner = runner;
    }

    public async Task<BudgetDashboardDto> HandleAsync(BudgetDashboardQuery query, CancellationToken ct)
    {
        IReadOnlyList<EventEnvelope> envelopes;
        CashLedgerState ledgerState;

        if (_runner != null)
        {
            ledgerState = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
                e => CashLedgerProjector.Project(e),
                ct: ct);
            envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        }
        else
        {
            envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
            ledgerState = CashLedgerProjector.Project(envelopes);
        }

        var envelopeList = envelopes.ToList();
        var budgetState = BudgetProjector.Project(envelopeList);
        var categoryState = CategoryProjector.Project(envelopeList);

        // Compute prior period for carry policy
        var priorYear = query.Month == 1 ? query.Year - 1 : query.Year;
        var priorMonth = query.Month == 1 ? 12 : query.Month - 1;

        // Prior state is the same full projection; carry logic just reads prior-period rows
        var utilizations = BudgetProjector.ComputeUtilization(
            budgetState, ledgerState, categoryState,
            query.Year, query.Month,
            budgetState, ledgerState);

        if (query.AccountId.HasValue)
        {
            utilizations = utilizations.Where(u =>
            {
                var b = budgetState.Budgets.TryGetValue(u.BudgetId, out var budget) ? budget : null;
                return b?.AccountId == query.AccountId.Value || !b?.ScopeType.Contains("account") == true;
            }).ToList();
        }

        var totalLimit = utilizations.Sum(u => u.LimitAmount);
        var totalActual = utilizations.Sum(u => u.ActualAmount);
        var totalRemaining = utilizations.Sum(u => u.RemainingAmount);

        return new BudgetDashboardDto(query.Year, query.Month, utilizations, totalLimit, totalActual, totalRemaining);
    }
}
