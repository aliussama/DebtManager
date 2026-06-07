using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

/// <summary>
/// A single entry in the obligation history timeline.
/// </summary>
public sealed record ObligationHistoryEntry(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    DateOnly EffectiveDate,
    string Description,
    Dictionary<string, object>? Details
);

/// <summary>
/// Complete history of an obligation.
/// </summary>
public sealed record ObligationHistoryResult(
    Guid ObligationId,
    string Name,
    string ObligationType,
    bool IsClosed,
    DateOnly? ClosureDate,
    IReadOnlyList<ObligationHistoryEntry> Timeline
);

public sealed record GetObligationHistoryQuery(Guid ObligationId);

/// <summary>
/// Use case: Get the complete event history of an obligation.
/// Provides full audit trail for transparency and explainability.
/// </summary>
public sealed class GetObligationHistoryHandler
{
    private readonly IEventStore _eventStore;

    public GetObligationHistoryHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task<ObligationHistoryResult> HandleAsync(
        GetObligationHistoryQuery query,
        CancellationToken ct = default)
    {
        var streamId = new StreamId(query.ObligationId);
        var events = await _eventStore.ReadStreamAsync(streamId, ct: ct);

        if (!events.Any())
            throw new InvalidOperationException($"Obligation {query.ObligationId} not found.");

        var timeline = new List<ObligationHistoryEntry>();
        string name = "";
        string obligationType = "";
        bool isClosed = false;
        DateOnly? closureDate = null;

        foreach (var envelope in events.OrderBy(e => e.OccurredAt))
        {
            var entry = MapToHistoryEntry(envelope, ref name, ref obligationType, ref isClosed, ref closureDate);
            if (entry != null)
            {
                timeline.Add(entry);
            }
        }

        return new ObligationHistoryResult(
            ObligationId: query.ObligationId,
            Name: name,
            ObligationType: obligationType,
            IsClosed: isClosed,
            ClosureDate: closureDate,
            Timeline: timeline.AsReadOnly()
        );
    }

    private static ObligationHistoryEntry? MapToHistoryEntry(
        EventEnvelope envelope,
        ref string name,
        ref string obligationType,
        ref bool isClosed,
        ref DateOnly? closureDate)
    {
        var details = new Dictionary<string, object>();

        switch (envelope.EventType)
        {
            case nameof(ObligationCreated):
                var created = System.Text.Json.JsonSerializer.Deserialize<ObligationCreated>(
                    envelope.PayloadJson, DomainJson.Options);
                if (created != null)
                {
                    name = created.Name;
                    obligationType = created.ObligationType;
                    details["principal"] = created.Principal.ToString();
                    details["currency"] = created.CurrencyCode;
                    return new ObligationHistoryEntry(
                        envelope.EventId.Value,
                        envelope.EventType,
                        envelope.OccurredAt,
                        envelope.EffectiveDate,
                        $"Obligation created: {created.Name}",
                        details
                    );
                }
                break;

            case nameof(ScheduleDefined):
                var schedule = System.Text.Json.JsonSerializer.Deserialize<ScheduleDefined>(
                    envelope.PayloadJson, DomainJson.Options);
                if (schedule != null)
                {
                    details["scheduleType"] = schedule.ScheduleType;
                    details["scheduleId"] = schedule.ScheduleId.ToString();
                    return new ObligationHistoryEntry(
                        envelope.EventId.Value,
                        envelope.EventType,
                        envelope.OccurredAt,
                        envelope.EffectiveDate,
                        $"Schedule defined: {schedule.ScheduleType}",
                        details
                    );
                }
                break;

            case nameof(ScheduleModified):
                var modified = System.Text.Json.JsonSerializer.Deserialize<ScheduleModified>(
                    envelope.PayloadJson, DomainJson.Options);
                if (modified != null)
                {
                    details["modificationType"] = modified.ModificationType.ToString();
                    details["reason"] = modified.Reason;
                    return new ObligationHistoryEntry(
                        envelope.EventId.Value,
                        envelope.EventType,
                        envelope.OccurredAt,
                        envelope.EffectiveDate,
                        $"Schedule modified: {modified.ModificationType}",
                        details
                    );
                }
                break;

            case nameof(ObligationClosed):
                var closed = System.Text.Json.JsonSerializer.Deserialize<ObligationClosed>(
                    envelope.PayloadJson, DomainJson.Options);
                if (closed != null)
                {
                    isClosed = true;
                    closureDate = closed.ClosureDate;
                    details["closureType"] = closed.ClosureType.ToString();
                    details["finalBalance"] = closed.FinalBalance.ToString();
                    if (closed.Reason != null) details["reason"] = closed.Reason;
                    return new ObligationHistoryEntry(
                        envelope.EventId.Value,
                        envelope.EventType,
                        envelope.OccurredAt,
                        envelope.EffectiveDate,
                        $"Obligation closed: {closed.ClosureType}",
                        details
                    );
                }
                break;

            case nameof(ObligationLinkedToInstitution):
                var linked = System.Text.Json.JsonSerializer.Deserialize<ObligationLinkedToInstitution>(
                    envelope.PayloadJson, DomainJson.Options);
                if (linked != null)
                {
                    details["institutionId"] = linked.InstitutionId.ToString();
                    if (linked.ProductCode != null) details["productCode"] = linked.ProductCode;
                    return new ObligationHistoryEntry(
                        envelope.EventId.Value,
                        envelope.EventType,
                        envelope.OccurredAt,
                        envelope.EffectiveDate,
                        "Linked to institution",
                        details
                    );
                }
                break;

            case nameof(ChargeWaived):
                var waived = System.Text.Json.JsonSerializer.Deserialize<ChargeWaived>(
                    envelope.PayloadJson, DomainJson.Options);
                if (waived != null)
                {
                    details["chargeType"] = waived.ChargeType;
                    details["waivedAmount"] = waived.WaivedAmount.ToString();
                    details["reason"] = waived.Reason;
                    return new ObligationHistoryEntry(
                        envelope.EventId.Value,
                        envelope.EventType,
                        envelope.OccurredAt,
                        envelope.EffectiveDate,
                        $"Charge waived: {waived.WaivedAmount}",
                        details
                    );
                }
                break;

            default:
                // Generic handling for payment events and others
                return new ObligationHistoryEntry(
                    envelope.EventId.Value,
                    envelope.EventType,
                    envelope.OccurredAt,
                    envelope.EffectiveDate,
                    envelope.EventType,
                    null
                );
        }

        return null;
    }
}
