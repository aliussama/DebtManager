using System.Text.Json;
using DebtManager.Domain.Cash;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Sync; // ISyncStore implemented by SqliteEventStore

namespace DebtManager.Application.UseCases;

public sealed record RecordIncomeCommand(
    decimal Amount,
    string CurrencyCode,
    DateOnly EffectiveDate,
    string Source,
    Guid? SourceId = null
);

public sealed class RecordIncomeHandler
{
    private readonly IEventStore _store;

    public RecordIncomeHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(RecordIncomeCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var sourceString = cmd.Source;

        // If SourceId is provided, validate it exists and is not archived
        if (cmd.SourceId.HasValue)
        {
            var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
            var sourceState = IncomeSourceProjector.Project(envelopes);
            var source = sourceState.TryGet(cmd.SourceId.Value);

            if (source == null)
                throw new InvalidOperationException($"Income source {cmd.SourceId.Value} not found.");
            if (source.IsArchived)
                throw new InvalidOperationException($"Income source '{source.Name}' is archived.");

            // Set Source string to source.Name for backward compatibility
            sourceString = source.Name;
        }

        var currency = cmd.CurrencyCode switch
        {
            "EGP" => Currency.EGP,
            "USD" => Currency.USD,
            "EUR" => Currency.EUR,
            _ => new Currency(cmd.CurrencyCode, 2)
        };

        var ev = new IncomeRecorded(
            DefaultAccount.AccountId,
            new Money(cmd.Amount, currency),
            cmd.EffectiveDate,
            sourceString
        );

        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(DefaultAccount.AccountId), // stream per account
            nameof(IncomeRecorded),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            actorUserId,
            deviceId,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(ev, DebtManager.Domain.ValueObjects.DomainJson.Options)
        );

        await _store.AppendAsync(env, ct);
    }
}
