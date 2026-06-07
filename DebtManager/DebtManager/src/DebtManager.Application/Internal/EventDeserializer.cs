using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Installments;
using DebtManager.Domain.Scheduling;

namespace DebtManager.Application.Internal;

internal static class EventDeserializer
{
    public static IEnumerable<IDomainEvent> ToDomainEvents(IEnumerable<EventEnvelope> envelopes)
    {
        foreach (var e in envelopes)
        {
            IDomainEvent? ev = null;

            var opt = DebtManager.Domain.ValueObjects.DomainJson.Options;

            System.Diagnostics.Debug.WriteLine(e.PayloadJson);

            if (e.EventType == nameof(ObligationCreated))
                ev = JsonSerializer.Deserialize<ObligationCreated>(e.PayloadJson, DebtManager.Domain.ValueObjects.DomainJson.Options);

            else if (e.EventType == nameof(PaymentMade))
            {

                // If payload contains "payment" field, it is StoredPaymentMade JSON — deserialize it as such.
                if (e.PayloadJson.Contains("\"payment\":", StringComparison.OrdinalIgnoreCase))
                {
                    var stored = JsonSerializer.Deserialize<StoredPaymentMade>(e.PayloadJson, opt)
                                 ?? throw new InvalidOperationException("StoredPaymentMade payload could not be deserialized.");

                    ev = stored.Payment ?? throw new InvalidOperationException("StoredPaymentMade.Payment was null (serialization mismatch).");
                }
                else
                {
                    // Legacy format (only if you ever wrote PaymentMade directly)
                    ev = JsonSerializer.Deserialize<PaymentMade>(e.PayloadJson, opt);
                }
            }

            else if (e.EventType == nameof(PaymentAllocated))
                ev = JsonSerializer.Deserialize<PaymentAllocated>(e.PayloadJson, opt);

            else if (e.EventType == nameof(PaymentReversed))
                ev = JsonSerializer.Deserialize<PaymentReversed>(e.PayloadJson, opt);

            else if (e.EventType == nameof(PaymentAllocationReversed))
                ev = JsonSerializer.Deserialize<PaymentAllocationReversed>(e.PayloadJson, opt);

            else if (e.EventType == nameof(IncomeRecorded))
                ev = JsonSerializer.Deserialize<IncomeRecorded>(e.PayloadJson, opt);

            else if (e.EventType == nameof(ExpenseRecorded))
                ev = JsonSerializer.Deserialize<ExpenseRecorded>(e.PayloadJson, opt);

            else if (e.EventType == nameof(ChargeAllocated))
                ev = JsonSerializer.Deserialize<ChargeAllocated>(e.PayloadJson, DebtManager.Domain.ValueObjects.DomainJson.Options);

            else if (e.EventType == nameof(PaymentUnapplied))
                ev = JsonSerializer.Deserialize<PaymentUnapplied>(e.PayloadJson, DebtManager.Domain.ValueObjects.DomainJson.Options);

            if (ev is not null)
                yield return ev;
        }
    }

    public static IEnumerable<ScheduleDefinition> ToSchedules(IEnumerable<EventEnvelope> envelopes)
        => envelopes
            .Where(e => e.EventType == "ScheduleDefined")
            .Select(e => JsonSerializer.Deserialize<ScheduleDefinition>(e.PayloadJson, DebtManager.Domain.ValueObjects.DomainJson.Options)!)
            .ToList();
}
