using System.Text.Json;
using DebtManager.Domain.Cash;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Sync; // ISyncStore implemented by SqliteEventStore

namespace DebtManager.Application.UseCases;

public sealed record RecordIncomeCommand(
    decimal Amount,
    string CurrencyCode,
    DateOnly EffectiveDate,
    string Source
);

public sealed class RecordIncomeHandler
{
    private readonly IEventStore _store;

    public RecordIncomeHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(RecordIncomeCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
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
            cmd.Source
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
