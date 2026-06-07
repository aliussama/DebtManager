using System.Text.Json;
using DebtManager.Domain.Events;

namespace DebtManager.Application.UseCases;

public sealed record AssignRulePackToObligationCommand(
    Guid ObligationId,
    string RulePackId,
    DateOnly EffectiveDate
);

public sealed class AssignRulePackToObligationHandler
{
    private readonly IEventStore _store;

    public AssignRulePackToObligationHandler(IEventStore store)
    {
        _store = store;
    }

    public async Task HandleAsync(AssignRulePackToObligationCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new RulePackAssignedToObligation(cmd.ObligationId, cmd.RulePackId, cmd.EffectiveDate);

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(cmd.ObligationId),
            nameof(RulePackAssignedToObligation),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            actorUserId,
            deviceId,
            CorrelationId: Guid.NewGuid(),
            CausationEventId: null,
            PayloadSchemaVersion: 1,
            PayloadJson: JsonSerializer.Serialize(ev, DebtManager.Domain.ValueObjects.DomainJson.Options)
        );

        await _store.AppendAsync(envelope, ct);
    }
}
