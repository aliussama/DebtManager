using DebtManager.Domain.Events;
using DebtManager.Domain.Installments;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Rules;
using DebtManager.Domain.Scheduling;
using DebtManager.Domain.Services.Serialization;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Services.Engines;

public sealed class ObligationSnapshotEngine
{
    private readonly IRuleEngine _ruleEngine;
    private readonly ScheduleExpanderV1 _expander = new();

    public ObligationSnapshotEngine(IRuleEngine ruleEngine)
    {
        _ruleEngine = ruleEngine;
    }

    public async Task<FinancialState> ComputeAsync(
        Guid obligationId,
        IReadOnlyList<EventEnvelope> obligationStreamUpToAsOf,
        IReadOnlyList<EventEnvelope> scheduleEnvelopesForObligation,
        DateOnly asOfDate,
        Currency baseCurrency,
        CancellationToken ct)
    {
        // 1) Projected events from obligation stream
        var projectedEvents = new List<ProjectedEvent>();
        foreach (var env in obligationStreamUpToAsOf)
        {
            var ev = EnvelopeDeserializer.ToDomainEvents(new[] { env }).FirstOrDefault();
            if (ev is not null)
                projectedEvents.Add(new ProjectedEvent(env, ev));
        }

        // 2) Parse schedules from schedule envelopes
        var schedules = EnvelopeDeserializer.ToSchedules(scheduleEnvelopesForObligation)
            .Where(s => s.ObligationId == obligationId)
            .ToList();

        // 3) Expand expected installments
        var expected = new List<ExpectedInstallment>();
        foreach (var s in schedules)
        {
            var expanded = await _expander.ExpandAsync(
                s,
                from: new DateOnly(asOfDate.Year - 1, 1, 1),
                to: new DateOnly(asOfDate.Year + 2, 12, 31),
                ct);

            expected.AddRange(expanded);
        }

        // 4) Rules-aware projection
        var projector = new RulesAwareFinancialProjector(_ruleEngine);

        return await projector.ReplayAsync(
            projectedEvents,
            expected,
            new ProjectionContext(asOfDate, baseCurrency),
            ct);
    }
}
