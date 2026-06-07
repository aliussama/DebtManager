using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

public sealed record CreateAccountCommand(
    Guid? AccountId,
    string Name,
    string AccountType,
    decimal OpeningBalance,
    string CurrencyCode,
    DateOnly EffectiveDate
);

public sealed class CreateAccountHandler
{
    private readonly IEventStore _store;

    public CreateAccountHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(CreateAccountCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var accountId = cmd.AccountId ?? Guid.NewGuid();

        var ev = new AccountCreated(
            accountId,
            cmd.Name,
            cmd.AccountType,
            cmd.CurrencyCode,
            cmd.OpeningBalance,
            cmd.EffectiveDate
        );

        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(accountId),
            nameof(AccountCreated),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            actorUserId,
            deviceId,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options)
        );

        await _store.AppendAsync(env, ct);
        return accountId;
    }
}
