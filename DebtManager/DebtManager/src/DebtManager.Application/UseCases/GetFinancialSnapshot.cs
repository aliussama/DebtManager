using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Rules;
using DebtManager.Domain.Services.Engines;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

public sealed class GetFinancialSnapshotHandler
{
    private readonly IEventStore _store;
    private readonly ObligationSnapshotEngine _engine;

    public GetFinancialSnapshotHandler(IEventStore store, IRuleEngine ruleEngine)
    {
        _store = store;
        _engine = new ObligationSnapshotEngine(ruleEngine);
    }

    public async Task<FinancialState> HandleAsync(Guid obligationId, DateOnly asOfDate, CancellationToken ct)
    {
        // 1) obligation stream up to asOf
        var obligationStream = await _store.ReadStreamAsync(new StreamId(obligationId), upTo: asOfDate, ct);

        // 2) schedules may live in separate streams -> scan all and filter schedule envelopes
        var all = await _store.ReadAllAsync(
            new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ct);

        var scheduleEnvelopes = all
            .Where(e => e.EventType.Contains("Schedule", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 3) compute using engine
        return await _engine.ComputeAsync(
            obligationId,
            obligationStream,
            scheduleEnvelopes,
            asOfDate,
            Currency.EGP,
            ct);
    }
}
