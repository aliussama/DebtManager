using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

public sealed record CreateObligationCommand(
    Guid ObligationId,
    string Name,
    string ObligationType,
    decimal PrincipalAmount,
    string CurrencyCode,
    DateOnly StartDate
);

public sealed class CreateObligationHandler
{
    private readonly IEventStore _store;

    public CreateObligationHandler(IEventStore store)
    {
        _store = store;
    }

    public async Task HandleAsync(CreateObligationCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var currency = cmd.CurrencyCode switch
        {
            "EGP" => Currency.EGP,
            "USD" => Currency.USD,
            "EUR" => Currency.EUR,
            _ => new Currency(cmd.CurrencyCode, 2)
        };

        var ev = new ObligationCreated(
            cmd.ObligationId,
            cmd.Name,
            cmd.ObligationType,
            new Money(cmd.PrincipalAmount, currency),
            cmd.StartDate,
            cmd.CurrencyCode);

        // In v1, we serialize typed events simply by storing EventType + JSON payload later.
        // For now, write envelope with payload_json minimal. We'll add real serialization next step.
        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(cmd.ObligationId),
            nameof(ObligationCreated),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            actorUserId,
            deviceId,
            CorrelationId: Guid.NewGuid(),
            CausationEventId: null,
            PayloadSchemaVersion: 1,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(ev, DebtManager.Domain.ValueObjects.DomainJson.Options)
        );

        await _store.AppendAsync(envelope, ct);
    }
}
