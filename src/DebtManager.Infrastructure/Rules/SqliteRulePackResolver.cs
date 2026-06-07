using System.Text.Json;
using DebtManager.Domain.Events;

namespace DebtManager.Infrastructure.Rules;

public interface IRulePackResolver
{
    Task<string?> ResolveRulePackIdAsync(Guid obligationId, DateOnly asOfDate, CancellationToken ct);
}

public sealed class SqliteRulePackResolver : IRulePackResolver
{
    private readonly DebtManager.Domain.Events.IEventStore _eventStore;

    public SqliteRulePackResolver(DebtManager.Domain.Events.IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task<string?> ResolveRulePackIdAsync(Guid obligationId, DateOnly asOfDate, CancellationToken ct)
    {
        var stream = await _eventStore.ReadStreamAsync(new StreamId(obligationId), upTo: asOfDate, ct);

        // pick latest assignment <= asOfDate
        RulePackAssignedToObligation? last = null;

        foreach (var e in stream)
        {
            if (e.EventType != nameof(RulePackAssignedToObligation)) continue;
            var ev = JsonSerializer.Deserialize<RulePackAssignedToObligation>(e.PayloadJson, DebtManager.Domain.ValueObjects.DomainJson.Options);
            if (ev is null) continue;

            if (ev.EffectiveDate <= asOfDate)
            {
                if (last is null || ev.EffectiveDate >= last.EffectiveDate)
                    last = ev;
            }
        }

        return last?.RulePackId;
    }
}
