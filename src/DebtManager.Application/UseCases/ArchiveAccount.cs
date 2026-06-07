using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

public sealed record ArchiveAccountCommand(
    Guid AccountId,
    DateOnly EffectiveDate,
    string Reason
);

public sealed class ArchiveAccountHandler
{
    private readonly IEventStore _store;

    public ArchiveAccountHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveAccountCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new AccountArchived(
            cmd.AccountId,
            cmd.EffectiveDate,
            cmd.Reason
        );

        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(cmd.AccountId),
            nameof(AccountArchived),
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
    }
}
