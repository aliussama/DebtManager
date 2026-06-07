using DebtManager.Domain.Events;
using DebtManager.Domain.Installments;
using DebtManager.Domain.Projections.Installments;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

public sealed class FinancialProjector
{
    public FinancialState Replay(
        IEnumerable<IDomainEvent> events,
        IReadOnlyList<ExpectedInstallment> expectedInstallments,
        ProjectionContext context)
    {
        var state = new FinancialState(context.BaseCurrency);

        // First: replay income, obligation creation, etc.
        foreach (var ev in events
                     .Where(e => e.EffectiveDate <= context.AsOfDate)
                     .OrderBy(e => e.EffectiveDate))
        {
            ApplyCore(state, ev);
        }

        // Second: build paid/outstanding from allocations (minus reversals)
        var allocations = events
            .OfType<PaymentAllocated>()
            .Where(a => a.EffectiveDate <= context.AsOfDate)
            .ToList();

        var reversals = events
            .OfType<PaymentAllocationReversed>()
            .Where(r => r.EffectiveDate <= context.AsOfDate)
            .ToList();

        var paidByInstallment = allocations
            .GroupBy(a => a.InstallmentKey)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var sum = Money.Zero(g.First().Amount.Currency);
                    foreach (var a in g)
                        sum = sum.Add(a.Amount);
                    return sum;
                });

        foreach (var g in reversals.GroupBy(r => r.InstallmentKey))
        {
            if (!paidByInstallment.TryGetValue(g.Key, out var current))
                continue;

            var sub = Money.Zero(current.Currency);
            foreach (var r in g)
                sub = sub.Add(r.Amount);

            var newPaid = current.Subtract(sub);
            if (newPaid.Amount < 0m)
                newPaid = Money.Zero(current.Currency);

            paidByInstallment[g.Key] = newPaid;
        }

        // Third: create installment states (THIS WAS MISSING)
        var classifier = new DebtManager.Domain.Projections.Installments.InstallmentClassifier();

        foreach (var inst in expectedInstallments.Where(i => i.DueDate <= context.AsOfDate))
        {
            var paid = paidByInstallment.TryGetValue(inst.InstallmentKey.Value, out var p)
                ? p
                : Money.Zero(inst.Amount.Currency);

            if (paid.Amount > inst.Amount.Amount)
                paid = inst.Amount;

            var outstanding = inst.Amount.Subtract(paid);
            var isFullyPaid = outstanding.Amount <= 0m;

            var (status, daysOverdue, risk) = classifier.Classify(
                inst.DueDate,
                isFullyPaid,
                context.AsOfDate);

            state.Installments.Add(new DebtManager.Domain.Projections.Installments.InstallmentState(
                inst.ObligationId,
                inst.InstallmentKey.Value,
                inst.DueDate,
                inst.Amount,
                paid,
                status,
                daysOverdue,
                risk));
        }

        return state;
    }
    public FinancialState Replay(
    IEnumerable<ProjectedEvent> events,
    IReadOnlyList<ExpectedInstallment> expectedInstallments,
    ProjectionContext context)
    {
        var state = new FinancialState(context.BaseCurrency);

        // Apply core events deterministically (use envelope OccurredAt)
        foreach (var pe in events
                     .Where(x => x.Event.EffectiveDate <= context.AsOfDate)
                     .OrderBy(x => x.Event.EffectiveDate)
                     .ThenBy(x => x.Envelope.OccurredAt))
        {
            ApplyCore(state, pe.Event, pe.Envelope);
        }

        // allocations + reversals logic stays the same but use pe.Event extraction
        var allocations = events
            .Select(x => x.Event)
            .OfType<PaymentAllocated>()
            .Where(a => a.EffectiveDate <= context.AsOfDate)
            .ToList();

        var reversals = events
            .Select(x => x.Event)
            .OfType<PaymentAllocationReversed>()
            .Where(r => r.EffectiveDate <= context.AsOfDate)
            .ToList();

        var paidByInstallment = allocations
            .GroupBy(a => a.InstallmentKey)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var sum = Money.Zero(g.First().Amount.Currency);
                    foreach (var a in g)
                        sum = sum.Add(a.Amount);
                    return sum;
                });

        foreach (var g in reversals.GroupBy(r => r.InstallmentKey))
        {
            if (!paidByInstallment.TryGetValue(g.Key, out var current))
                continue;

            var sub = Money.Zero(current.Currency);
            foreach (var r in g)
                sub = sub.Add(r.Amount);

            var newPaid = current.Subtract(sub);
            if (newPaid.Amount < 0m)
                newPaid = Money.Zero(current.Currency);

            paidByInstallment[g.Key] = newPaid;
        }

        var classifier = new InstallmentClassifier();

        foreach (var inst in expectedInstallments.Where(i => i.DueDate <= context.AsOfDate))
        {
            var paid = paidByInstallment.TryGetValue(inst.InstallmentKey.Value, out var p)
                ? p
                : Money.Zero(inst.Amount.Currency);

            if (paid.Amount > inst.Amount.Amount)
                paid = inst.Amount;

            var outstanding = inst.Amount.Subtract(paid);
            var isFullyPaid = outstanding.Amount <= 0m;

            var (status, daysOverdue, risk) = classifier.Classify(
                inst.DueDate,
                isFullyPaid,
                context.AsOfDate);

            state.Installments.Add(new InstallmentState(
                inst.ObligationId,
                inst.InstallmentKey.Value,
                inst.DueDate,
                inst.Amount,
                paid,
                status,
                daysOverdue,
                risk));
        }

        return state;
    }
    private static void ApplyCore(FinancialState state, IDomainEvent ev)
    {
        switch (ev)
        {
            case IncomeReceived income:
                {
                    state.RegisterIncome(income.Amount);
                    break;
                }

            case PaymentMade payment:
                {
                    state.RegisterPayment(payment.Amount);
                    if (state.Obligations.TryGetValue(payment.ObligationId, out var obligation))
                        obligation.ApplyPayment(payment.Amount);
                    if (payment.Amount.Currency is null)
                        throw new InvalidOperationException($"PaymentMade has null currency. Reference={payment.Reference}");
                    break;
                }

            case ObligationCreated created:
                {
                    var obligation = new ObligationState(
                        created.ObligationId,
                        created.Name,
                        created.Principal);

                    state.RegisterObligation(obligation);

                    state.Audit.Add(new DebtManager.Domain.Audit.AuditEntry(
                        At: DateTimeOffset.UtcNow,
                        EffectiveDate: created.EffectiveDate,
                        Category: "Obligation",
                        Message: $"Obligation created: {created.Name} ({created.ObligationType}) principal {created.Principal}.",
                        RelatedEventId: null,
                        ObligationId: created.ObligationId
                    ));

                    break;
                }

            case PaymentReversed rev:
                {
                    state.RegisterPayment(rev.Amount.Multiply(-1m));
                    if (state.Obligations.TryGetValue(rev.ObligationId, out var obligation))
                        obligation.ApplyPayment(rev.Amount.Multiply(-1m));

                    state.Audit.Add(new DebtManager.Domain.Audit.AuditEntry(
                        At: DateTimeOffset.UtcNow,
                        EffectiveDate: rev.EffectiveDate,
                        Category: "Payment",
                        Message: $"Payment reversed: {rev.Amount} (reason: {rev.Reason}).",
                        RelatedEventId: rev.OriginalPaymentEventId,
                        ObligationId: rev.ObligationId,
                        Severity: "warning"
                    ));
                    break;
                }
            case DebtManager.Domain.Events.ChargeAllocated ca:
                state.RegisterChargePayment(ca.ChargeId, ca.Amount);
                break;

            case DebtManager.Domain.Events.PaymentUnapplied pu:
                state.RegisterUnapplied(pu.Amount);
                break;

                // PaymentAllocated is handled in installment projection part
        }
    }
    private void ApplyCore(FinancialState state, IDomainEvent ev, EventEnvelope env)
    {
        ApplyCore(state, ev); // keep existing logic

        // deterministic audit emission (use env.OccurredAt and env.EventId)
        switch (ev)
        {
            case ObligationCreated oc:
                state.Audit.Add(new DebtManager.Domain.Audit.AuditEntry(
                    At: env.OccurredAt,
                    EffectiveDate: oc.EffectiveDate,
                    Category: "Obligation",
                    Message: $"Obligation created: {oc.Name} ({oc.ObligationType}) principal {oc.Principal}.",
                    RelatedEventId: env.EventId.Value,
                    ObligationId: oc.ObligationId,
                    Severity: "info"
                ));
                break;

            case PaymentMade pm:
                state.Audit.Add(new DebtManager.Domain.Audit.AuditEntry(
                    At: env.OccurredAt,
                    EffectiveDate: pm.EffectiveDate,
                    Category: "Payment",
                    Message: $"Payment recorded: {pm.Amount} (ref: {pm.Reference ?? "n/a"}).",
                    RelatedEventId: env.EventId.Value,
                    ObligationId: pm.ObligationId,
                    Severity: "info"
                ));
                break;

            case PaymentReversed pr:
                state.Audit.Add(new DebtManager.Domain.Audit.AuditEntry(
                    At: env.OccurredAt,
                    EffectiveDate: pr.EffectiveDate,
                    Category: "Payment",
                    Message: $"Payment reversed: {pr.Amount} (reason: {pr.Reason}).",
                    RelatedEventId: env.EventId.Value,
                    ObligationId: pr.ObligationId,
                    Severity: "warning"
                ));
                break;
        }
    }
}
