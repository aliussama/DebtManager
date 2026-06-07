using DebtManager.Application.Internal;
using DebtManager.Domain.Allocation;
using DebtManager.Domain.Events;
using DebtManager.Domain.Installments;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Projections.Charges;
using DebtManager.Domain.Rules;
using DebtManager.Domain.Scheduling;
using DebtManager.Domain.Services;
using DebtManager.Domain.Services.Allocation;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Rules;
using System.Text.Json;

namespace DebtManager.Application.UseCases;

public sealed record RecordPaymentCommand(
    Guid ObligationId,
    decimal Amount,
    string CurrencyCode,
    DateOnly EffectiveDate,
    string? Reference
);

public sealed class RecordPaymentHandler
{
    private readonly IEventStore _store;
    private readonly IRuleEngine _ruleEngine;
    private readonly ScheduleExpanderV1 _expander = new();
    private readonly PaymentAllocationService _allocator =
        new(new OldestDueFirstAllocator());

    public RecordPaymentHandler(IEventStore store, IRuleEngine ruleEngine)
    {
        _store = store;
        _ruleEngine = ruleEngine;
    }

    public async Task HandleAsync(
        RecordPaymentCommand cmd,
        Guid actorUserId,
        Guid deviceId,
        CancellationToken ct)
    {
        var currency = cmd.CurrencyCode switch
        {
            "EGP" => Currency.EGP,
            "USD" => Currency.USD,
            "EUR" => Currency.EUR,
            _ => new Currency(cmd.CurrencyCode, 2)
        };

        // 1) Load stream up to payment effective date
        var stream = await _store.ReadStreamAsync(
            new StreamId(cmd.ObligationId),
            upTo: cmd.EffectiveDate,
            ct);

        // 2) Deserialize events + schedules
        var domainEvents = EventDeserializer.ToDomainEvents(stream).ToList();
        var schedules = EventDeserializer.ToSchedules(stream).ToList();

        // 3) Expand expected installments
        var expected = new List<ExpectedInstallment>();
        foreach (var s in schedules)
        {
            var expanded = await _expander.ExpandAsync(
                s,
                from: new DateOnly(cmd.EffectiveDate.Year - 1, 1, 1),
                to: new DateOnly(cmd.EffectiveDate.Year + 2, 12, 31),
                ct);

            expected.AddRange(expanded);
        }

        // 4) Existing allocations (installment allocations)
        var existingAllocations = domainEvents
            .OfType<PaymentAllocated>()
            .ToList();

        // 5) Create PaymentMade
        var payment = new PaymentMade(
            cmd.ObligationId,
            new Money(cmd.Amount, currency),
            cmd.EffectiveDate,
            cmd.Reference);

        var paymentEventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        await _store.AppendAsync(new EventEnvelope(
            new EventId(paymentEventId),
            new StreamId(cmd.ObligationId),
            nameof(PaymentMade),
            DateTimeOffset.UtcNow,
            payment.EffectiveDate,
            actorUserId,
            deviceId,
            correlationId,
            null,
            2,
            JsonSerializer.Serialize(new StoredPaymentMade(paymentEventId, payment), DebtManager.Domain.ValueObjects.DomainJson.Options)
        ), ct);

        // 6) Compute charges as-of payment date using injected rule engine (works for real store and scenario store)
        var projector = new RulesAwareFinancialProjector(_ruleEngine);

        // Build projected events from the stream (same as snapshot handler)
        var projectedEvents = new List<ProjectedEvent>();
        foreach (var env in stream)
        {
            var ev = EventDeserializer.ToDomainEvents(new[] { env }).FirstOrDefault();
            if (ev is not null)
                projectedEvents.Add(new ProjectedEvent(env, ev));
        }

        var state = await projector.ReplayAsync(
            projectedEvents,
            expected,
            new ProjectionContext(cmd.EffectiveDate, currency),
            ct);

        // 7) Allocate to charges first
        var policy = new AllocationPolicyV1();
        var chargeAlloc = policy.AllocateChargesFirst(state, payment.Amount);

        foreach (var (chargeId, amt) in chargeAlloc.ChargeAllocations)
        {
            var ev = new ChargeAllocated(
                ObligationId: cmd.ObligationId,
                ChargeId: chargeId,
                PaymentEventId: paymentEventId,
                Amount: amt,
                EffectiveDate: cmd.EffectiveDate
            );

            await _store.AppendAsync(new EventEnvelope(
                new EventId(Guid.NewGuid()),
                new StreamId(cmd.ObligationId),
                nameof(ChargeAllocated),
                DateTimeOffset.UtcNow,
                ev.EffectiveDate,
                actorUserId,
                deviceId,
                correlationId,
                paymentEventId,
                1,
                JsonSerializer.Serialize(ev, DebtManager.Domain.ValueObjects.DomainJson.Options)
            ), ct);
        }

        // 8) Allocate remaining to installments (existing allocator)
        var paymentForInstallments = new PaymentMade(
            cmd.ObligationId,
            chargeAlloc.RemainingForInstallments,
            cmd.EffectiveDate,
            cmd.Reference);

        var allocations = _allocator.AllocatePayment(
            paymentEventId,
            paymentForInstallments,
            expected,
            existingAllocations,
            cmd.EffectiveDate);

        foreach (var alloc in allocations)
        {
            await _store.AppendAsync(new EventEnvelope(
                new EventId(Guid.NewGuid()),
                new StreamId(cmd.ObligationId),
                nameof(PaymentAllocated),
                DateTimeOffset.UtcNow,
                alloc.EffectiveDate,
                actorUserId,
                deviceId,
                correlationId,
                paymentEventId,
                1,
                JsonSerializer.Serialize(alloc, DebtManager.Domain.ValueObjects.DomainJson.Options)
            ), ct);
        }
        // 9) If anything remains, record unapplied (prepayment/overpayment)
        var allocatedToInstallments = allocations.Sum(a => a.Amount.Amount);
        var remaining = chargeAlloc.RemainingForInstallments.Amount - allocatedToInstallments;

        if (remaining > 0m)
        {
            var unapplied = new PaymentUnapplied(
                ObligationId: cmd.ObligationId,
                PaymentEventId: paymentEventId,
                Amount: new Money(remaining, currency),
                EffectiveDate: cmd.EffectiveDate,
                Reason: "Overpayment / prepayment"
            );

            await _store.AppendAsync(new EventEnvelope(
                new EventId(Guid.NewGuid()),
                new StreamId(cmd.ObligationId),
                nameof(PaymentUnapplied),
                DateTimeOffset.UtcNow,
                unapplied.EffectiveDate,
                actorUserId,
                deviceId,
                correlationId,
                paymentEventId,
                1,
                JsonSerializer.Serialize(unapplied, DebtManager.Domain.ValueObjects.DomainJson.Options)
            ), ct);
        }
    }
}