using DebtManager.Application.Internal;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Rules;
using DebtManager.Domain.Scheduling;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Simulation;
using System.Text.Json;

namespace DebtManager.Application.Simulation;

public sealed class SimulateScenarioHandler
{
    private readonly IEventStore _realStore;
    private readonly IRuleEngine _ruleEngine;
    private readonly ScheduleExpanderV1 _expander = new();

    public SimulateScenarioHandler(IEventStore realStore, IRuleEngine ruleEngine)
    {
        _realStore = realStore;
        _ruleEngine = ruleEngine;
    }
    private static IEnumerable<EventEnvelope> LinkedAllocationEnvelopes(IEnumerable<EventEnvelope> envelopes, Guid paymentEventId)
    {
        return envelopes.Where(e =>
            e.EventType == nameof(PaymentAllocated) &&
            e.CausationEventId.HasValue &&
            e.CausationEventId.Value == paymentEventId);
    }

    public async Task<ScenarioResult> HandleAsync(
        SimulateScenarioCommand cmd,
        Guid actorUserId,
        Guid deviceId,
        CancellationToken ct)
    {
        // 1) Baseline envelopes
        var baselineStream = await _realStore.ReadStreamAsync(new StreamId(cmd.ObligationId), upTo: cmd.HorizonEndDate, ct);

        // 2) Build expected installments from schedules (same logic as snapshot)
        var schedules = EventDeserializer.ToSchedules(baselineStream).ToList();
        if (schedules.Count == 0)
            throw new InvalidOperationException("Simulation: No schedules found in stream. Expected ScheduleDefined events before horizon.");
        var expected = new List<DebtManager.Domain.Installments.ExpectedInstallment>();

        foreach (var s in schedules)
        {
            var expanded = await _expander.ExpandAsync(
                s,
                from: new DateOnly(cmd.AsOfDate.Year - 1, 1, 1),
                to: cmd.HorizonEndDate,
                ct);

            expected.AddRange(expanded);
        }

        // 3) Baseline domain events for projection
        var baselineDomainEvents = EventDeserializer.ToDomainEvents(baselineStream)
            .Where(e => e is not null)
            .ToList();

        // Baseline projection (rules-aware)
        var baselineProjector = new RulesAwareFinancialProjector(_ruleEngine);
        var baselineState = await baselineProjector.ReplayAsync(
            baselineDomainEvents,
            expected,
            new ProjectionContext(cmd.AsOfDate, Currency.EGP),
            ct);

        // 4) Scenario store = baseline + hypothetical events
        var scenarioStore = new InMemoryEventStore(baselineStream);

        var recordPayment = new DebtManager.Application.UseCases.RecordPaymentHandler(scenarioStore, _ruleEngine);

        foreach (var h in cmd.Hypotheses)
        {
            switch (h.Type)
            {
                case HypothesisType.ExtraPayment:
                    {
                        if (h.Amount is null || string.IsNullOrWhiteSpace(h.CurrencyCode))
                            throw new InvalidOperationException("ExtraPayment requires Amount and CurrencyCode.");

                        await recordPayment.HandleAsync(
                            new DebtManager.Application.UseCases.RecordPaymentCommand(
                                ObligationId: cmd.ObligationId,
                                Amount: h.Amount.Value,
                                CurrencyCode: h.CurrencyCode!,
                                EffectiveDate: h.EffectiveDate,
                                Reference: h.Reference ?? "Scenario ExtraPayment"
                            ),
                            actorUserId, deviceId, ct);

                        break;
                    }

                case HypothesisType.OneTimeExpense:
                    {
                        // Model expense as a payment to a synthetic "expense obligation" later.
                        // For v1: record as PaymentMade against the same obligation as a cash drain.
                        if (h.Amount is null || string.IsNullOrWhiteSpace(h.CurrencyCode))
                            throw new InvalidOperationException("OneTimeExpense requires Amount and CurrencyCode.");

                        await recordPayment.HandleAsync(
                            new DebtManager.Application.UseCases.RecordPaymentCommand(
                                ObligationId: cmd.ObligationId,
                                Amount: h.Amount.Value,
                                CurrencyCode: h.CurrencyCode!,
                                EffectiveDate: h.EffectiveDate,
                                Reference: h.Reference ?? "Scenario OneTimeExpense"
                            ),
                            actorUserId, deviceId, ct);

                        break;
                    }

                case HypothesisType.MissPayment:
                    {
                        if (string.IsNullOrWhiteSpace(h.PaymentReferenceContains))
                            throw new InvalidOperationException("MissPayment requires PaymentReferenceContains.");

                        var scenarioStreamNow = await scenarioStore.ReadStreamAsync(new StreamId(cmd.ObligationId), upTo: cmd.HorizonEndDate, ct);

                        var payments = scenarioStreamNow
                            .Where(e => e.EventType == nameof(PaymentMade))
                            .Select(e => JsonSerializer.Deserialize<DebtManager.Application.Internal.StoredPaymentMade>(e.PayloadJson, DebtManager.Domain.ValueObjects.DomainJson.Options)!)
                            .Where(sp => (sp.Payment.Reference ?? "").Contains(h.PaymentReferenceContains!, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (payments.Count == 0)
                            throw new InvalidOperationException("No matching payment found to miss.");

                        var target = payments.Last(); // most recent match
                        var correlationId = Guid.NewGuid();

                        // 1) reverse payment
                        var rev = new PaymentReversed(
                            OriginalPaymentEventId: target.PaymentEventId,
                            ObligationId: cmd.ObligationId,
                            Amount: target.Payment.Amount,
                            EffectiveDate: h.EffectiveDate,
                            Reason: h.Reference ?? "Scenario MissPayment");

                        await scenarioStore.AppendAsync(new EventEnvelope(
                            new EventId(Guid.NewGuid()),
                            new StreamId(cmd.ObligationId),
                            nameof(PaymentReversed),
                            DateTimeOffset.UtcNow,
                            rev.EffectiveDate,
                            actorUserId,
                            deviceId,
                            CorrelationId: correlationId,
                            CausationEventId: target.PaymentEventId,
                            PayloadSchemaVersion: 1,
                            PayloadJson: JsonSerializer.Serialize(rev, DebtManager.Domain.ValueObjects.DomainJson.Options)
                        ), ct);

                        // 2) reverse allocations linked to this payment
                        var allocEnvelopes = LinkedAllocationEnvelopes(scenarioStreamNow, target.PaymentEventId).ToList();

                        foreach (var aenv in allocEnvelopes)
                        {
                            var alloc = JsonSerializer.Deserialize<PaymentAllocated>(aenv.PayloadJson, DebtManager.Domain.ValueObjects.DomainJson.Options)!;

                            var arev = new PaymentAllocationReversed(
                                OriginalPaymentEventId: target.PaymentEventId,
                                ObligationId: cmd.ObligationId,
                                InstallmentKey: alloc.InstallmentKey,
                                Amount: alloc.Amount,
                                EffectiveDate: h.EffectiveDate,
                                Reason: h.Reference ?? "Scenario MissPayment (alloc reverse)"
                            );

                            await scenarioStore.AppendAsync(new EventEnvelope(
                                new EventId(Guid.NewGuid()),
                                new StreamId(cmd.ObligationId),
                                nameof(PaymentAllocationReversed),
                                DateTimeOffset.UtcNow,
                                arev.EffectiveDate,
                                actorUserId,
                                deviceId,
                                CorrelationId: correlationId,
                                CausationEventId: target.PaymentEventId,
                                PayloadSchemaVersion: 1,
                                PayloadJson: JsonSerializer.Serialize(arev, DebtManager.Domain.ValueObjects.DomainJson.Options)
                            ), ct);
                        }

                        break;
                    }

                case HypothesisType.DelayedPayment:
                    {
                        if (string.IsNullOrWhiteSpace(h.PaymentReferenceContains) || h.NewEffectiveDate is null)
                            throw new InvalidOperationException("DelayedPayment requires PaymentReferenceContains and NewEffectiveDate.");

                        var scenarioStreamNow = await scenarioStore.ReadStreamAsync(new StreamId(cmd.ObligationId), upTo: cmd.HorizonEndDate, ct);

                        var payments = scenarioStreamNow
                            .Where(e => e.EventType == nameof(PaymentMade))
                            .Select(e => JsonSerializer.Deserialize<DebtManager.Application.Internal.StoredPaymentMade>(e.PayloadJson, DebtManager.Domain.ValueObjects.DomainJson.Options)!)
                            .Where(sp => (sp.Payment.Reference ?? "").Contains(h.PaymentReferenceContains!, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (payments.Count == 0)
                            throw new InvalidOperationException("No matching payment found to delay.");

                        var target = payments.Last();
                        var correlationId = Guid.NewGuid();

                        // 1) reverse original payment
                        var rev = new PaymentReversed(
                            OriginalPaymentEventId: target.PaymentEventId,
                            ObligationId: cmd.ObligationId,
                            Amount: target.Payment.Amount,
                            EffectiveDate: h.EffectiveDate,
                            Reason: h.Reference ?? "Scenario DelayedPayment (reverse)");

                        await scenarioStore.AppendAsync(new EventEnvelope(
                            new EventId(Guid.NewGuid()),
                            new StreamId(cmd.ObligationId),
                            nameof(PaymentReversed),
                            DateTimeOffset.UtcNow,
                            rev.EffectiveDate,
                            actorUserId,
                            deviceId,
                            CorrelationId: correlationId,
                            CausationEventId: target.PaymentEventId,
                            PayloadSchemaVersion: 1,
                            PayloadJson: JsonSerializer.Serialize(rev, DebtManager.Domain.ValueObjects.DomainJson.Options)
                        ), ct);

                        // 2) reverse allocations linked to original payment
                        var allocEnvelopes = LinkedAllocationEnvelopes(scenarioStreamNow, target.PaymentEventId).ToList();

                        foreach (var aenv in allocEnvelopes)
                        {
                            var alloc = JsonSerializer.Deserialize<PaymentAllocated>(aenv.PayloadJson, DebtManager.Domain.ValueObjects.DomainJson.Options)!;

                            var arev = new PaymentAllocationReversed(
                                OriginalPaymentEventId: target.PaymentEventId,
                                ObligationId: cmd.ObligationId,
                                InstallmentKey: alloc.InstallmentKey,
                                Amount: alloc.Amount,
                                EffectiveDate: h.EffectiveDate,
                                Reason: h.Reference ?? "Scenario DelayedPayment (alloc reverse)"
                            );

                            await scenarioStore.AppendAsync(new EventEnvelope(
                                new EventId(Guid.NewGuid()),
                                new StreamId(cmd.ObligationId),
                                nameof(PaymentAllocationReversed),
                                DateTimeOffset.UtcNow,
                                arev.EffectiveDate,
                                actorUserId,
                                deviceId,
                                CorrelationId: correlationId,
                                CausationEventId: target.PaymentEventId,
                                PayloadSchemaVersion: 1,
                                PayloadJson: JsonSerializer.Serialize(arev, DebtManager.Domain.ValueObjects.DomainJson.Options)
                            ), ct);
                        }

                        // 3) re-add payment at new effective date using recordPayment (creates new allocations)
                        await recordPayment.HandleAsync(
                            new DebtManager.Application.UseCases.RecordPaymentCommand(
                                ObligationId: cmd.ObligationId,
                                Amount: target.Payment.Amount.Amount,
                                CurrencyCode: target.Payment.Amount.Currency.Code,
                                EffectiveDate: h.NewEffectiveDate.Value,
                                Reference: (target.Payment.Reference ?? "Payment") + " (Delayed)"
                            ),
                            actorUserId,
                            deviceId,
                            ct
                        );

                        break;
                    }

                case HypothesisType.IncomeShock:
                    {
                        // Income handling requires global income events; current pipeline is obligation-scoped.
                        // We'll add Income streams in Step 8.5 next.
                        throw new NotSupportedException("IncomeShock requires income streams support (next step).");
                    }
            }
        }

        // 5) Project scenario using scenarioStore
        var scenarioStream = await scenarioStore.ReadStreamAsync(new StreamId(cmd.ObligationId), upTo: cmd.HorizonEndDate, ct);

        var scenarioDomainEvents = EventDeserializer.ToDomainEvents(scenarioStream)
            .Where(e => e is not null)
            .ToList();

        var scenarioProjector = new RulesAwareFinancialProjector(_ruleEngine);
        var scenarioState = await scenarioProjector.ReplayAsync(
            scenarioDomainEvents,
            expected,
            new ProjectionContext(cmd.AsOfDate, Currency.EGP),
            ct);

        // 6) Diff summary (v1)
        var diff = new ScenarioDiff(
            BaselineTotalPayments: baselineState.TotalPayments.Amount,
            ScenarioTotalPayments: scenarioState.TotalPayments.Amount,
            BaselineChargesCount: baselineState.Charges.Count,
            ScenarioChargesCount: scenarioState.Charges.Count,
            BaselineOverdueInstallments: baselineState.Installments.Count(i => i.Status == DebtManager.Domain.Projections.Installments.InstallmentStatus.Overdue),
            ScenarioOverdueInstallments: scenarioState.Installments.Count(i => i.Status == DebtManager.Domain.Projections.Installments.InstallmentStatus.Overdue)
        );

        return new ScenarioResult(baselineState, scenarioState, diff);
    }
}
