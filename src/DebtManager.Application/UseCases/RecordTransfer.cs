using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

public sealed record RecordTransferCommand(
    Guid? TransferId,
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount,
    string CurrencyCode,
    DateOnly EffectiveDate,
    string Reference
);

public sealed class RecordTransferHandler
{
    private readonly IEventStore _store;

    public RecordTransferHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(RecordTransferCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        if (cmd.FromAccountId == cmd.ToAccountId)
            throw new InvalidOperationException("Cannot transfer to the same account");

        if (cmd.Amount <= 0)
            throw new InvalidOperationException("Transfer amount must be positive");

        var transferId = cmd.TransferId ?? Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var ev = new TransferRecorded(
            transferId,
            cmd.FromAccountId,
            cmd.ToAccountId,
            cmd.Amount,
            cmd.CurrencyCode,
            cmd.EffectiveDate,
            cmd.Reference
        );

        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(cmd.FromAccountId),
            nameof(TransferRecorded),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            actorUserId,
            deviceId,
            correlationId,
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options)
        );

        await _store.AppendAsync(env, ct);
        return transferId;
    }
}
