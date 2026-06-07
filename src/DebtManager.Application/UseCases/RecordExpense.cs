using System.Text.Json;
using DebtManager.Application.Identity;
using DebtManager.Domain.Cash;
using DebtManager.Domain.Events;
using DebtManager.Domain.Identity;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

public sealed record RecordExpenseCommand(
    decimal Amount,
    string CurrencyCode,
    DateOnly EffectiveDate,
    string Category,
    string Notes
);

public sealed class RecordExpenseHandler
{
    private readonly IEventStore _store;

    public RecordExpenseHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(RecordExpenseCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct, IdentityContext? identityContext = null)
    {
        identityContext?.Require(VaultPermission.PERM_WRITE_CASH);

        var currency = cmd.CurrencyCode switch
        {
            "EGP" => Currency.EGP,
            "USD" => Currency.USD,
            "EUR" => Currency.EUR,
            _ => new Currency(cmd.CurrencyCode, 2)
        };

        var ev = new ExpenseRecorded(
            DefaultAccount.AccountId,
            new Money(cmd.Amount, currency),
            cmd.EffectiveDate,
            cmd.Category,
            cmd.Notes
        );

        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(DefaultAccount.AccountId),
            nameof(ExpenseRecorded),
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
