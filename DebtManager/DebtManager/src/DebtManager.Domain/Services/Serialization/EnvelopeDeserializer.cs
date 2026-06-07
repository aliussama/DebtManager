using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Scheduling;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Services.Serialization;

public static class EnvelopeDeserializer
{
    public static IEnumerable<IDomainEvent> ToDomainEvents(IEnumerable<EventEnvelope> envelopes)
    {
        foreach (var e in envelopes)
        {
            IDomainEvent? ev = null;
            var opt = DomainJson.Options;

            if (e.EventType == nameof(ObligationCreated))
                ev = JsonSerializer.Deserialize<ObligationCreated>(e.PayloadJson, opt);

            else if (e.EventType == nameof(PaymentMade))
            {
                // v2+: payload is wrapped like:
                // { "paymentEventId": "...", "payment": { ...PaymentMade... } }
                // v1: payload is directly PaymentMade
                if (e.PayloadSchemaVersion >= 2)
                {
                    using var doc = JsonDocument.Parse(e.PayloadJson);

                    if (doc.RootElement.TryGetProperty("payment", out var paymentEl) ||
                        doc.RootElement.TryGetProperty("Payment", out paymentEl))
                    {
                        ev = JsonSerializer.Deserialize<PaymentMade>(paymentEl.GetRawText(), opt);
                    }
                    else
                    {
                        // fallback: if schema says wrapped but payload isn't, try direct
                        ev = JsonSerializer.Deserialize<PaymentMade>(e.PayloadJson, opt);
                    }
                }
                else
                {
                    ev = JsonSerializer.Deserialize<PaymentMade>(e.PayloadJson, opt);
                }

                // extra safety fallback for any historical weirdness
                if (ev is null && e.PayloadJson.Contains("\"payment\":", StringComparison.OrdinalIgnoreCase))
                {
                    using var doc = JsonDocument.Parse(e.PayloadJson);
                    if (doc.RootElement.TryGetProperty("payment", out var paymentEl) ||
                        doc.RootElement.TryGetProperty("Payment", out paymentEl))
                    {
                        ev = JsonSerializer.Deserialize<PaymentMade>(paymentEl.GetRawText(), opt);
                    }
                }
            }

            else if (e.EventType == nameof(PaymentAllocated))
                ev = JsonSerializer.Deserialize<PaymentAllocated>(e.PayloadJson, opt);

            else if (e.EventType == nameof(ChargeAllocated))
                ev = JsonSerializer.Deserialize<ChargeAllocated>(e.PayloadJson, opt);

            else if (e.EventType == nameof(PaymentUnapplied))
                ev = JsonSerializer.Deserialize<PaymentUnapplied>(e.PayloadJson, opt);

            else if (e.EventType == nameof(RulePackAssignedToObligation))
                ev = JsonSerializer.Deserialize<RulePackAssignedToObligation>(e.PayloadJson, opt);

            else if (e.EventType == "ScheduleDefined")
            {
                // Schedule events are not part of IDomainEvent stream replay (projection doesn’t apply them directly),
                // so we don't yield them here.
                ev = null;
            }

            else if (e.EventType == nameof(IncomeRecorded))
                ev = JsonSerializer.Deserialize<IncomeRecorded>(e.PayloadJson, opt);

            else if (e.EventType == nameof(ExpenseRecorded))
                ev = JsonSerializer.Deserialize<ExpenseRecorded>(e.PayloadJson, opt);

            if (ev is not null)
                yield return ev;
        }
    }

    public static IEnumerable<ScheduleDefinition> ToSchedules(IEnumerable<EventEnvelope> envelopes)
    {
        foreach (var e in envelopes)
        {
            if (e.EventType != "ScheduleDefined") continue;

            var def = JsonSerializer.Deserialize<ScheduleDefinition>(e.PayloadJson, DomainJson.Options);
            if (def is null) continue;

            yield return def;
        }
    }
}
