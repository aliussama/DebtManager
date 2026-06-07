using DebtManager.Domain.Projections.Charges;
using DebtManager.Domain.Installments;
using DebtManager.Domain.Projections.Installments;
using DebtManager.Domain.Rules;
using DebtManager.Domain.Services.Rules;
using DebtManager.Domain.ValueObjects;
using DebtManager.Domain.Audit;
using System.Text.Json;

namespace DebtManager.Domain.Projections;

public sealed class RulesAwareFinancialProjector
{
    private readonly FinancialProjector _baseProjector = new();
    private readonly IRuleEngine _ruleEngine;
    private readonly RuleContextBuilder _ctxBuilder = new();
    private readonly RuleEffectMapper _mapper = new();

    public RulesAwareFinancialProjector(IRuleEngine ruleEngine)
    {
        _ruleEngine = ruleEngine;
    }

    public async Task<FinancialState> ReplayAsync(
        IEnumerable<Events.IDomainEvent> events,
        IReadOnlyList<ExpectedInstallment> expectedInstallments,
        ProjectionContext context,
        CancellationToken ct)
    {
        // 1) base replay (income/payments/obligations/installments statuses)
        var state = _baseProjector.Replay(events, expectedInstallments, context);

        // 2) rule evaluation for each installment (only those not fully paid)
        var currency = context.BaseCurrency;

        foreach (var inst in state.Installments)
        {
            if (inst.IsFullyPaid) continue;

            // Only evaluate when meaningful:
            // upcoming, due today, overdue. (Paid is already skipped.)
            var ctx = _ctxBuilder.BuildForInstallment(inst, context.AsOfDate, currency.Code);

            var (effects, trace) = await _ruleEngine.EvaluateAsync(ctx, ct);

            var charges = _mapper.Map(ctx, effects, trace, currency);
            state.Charges.AddRange(charges);

            // 🔍 AUDIT: record why rules produced these charges
            foreach (var charge in charges)
            {
                state.Audit.Add(new DebtManager.Domain.Audit.AuditEntry(
                    At: DateTimeOffset.UtcNow,
                    EffectiveDate: charge.EffectiveDate,
                    Category: "RuleEngine",
                    Message: $"Charge applied: {charge.Label} {charge.Amount} on installment due {inst.DueDate:yyyy-MM-dd}. Effects={effects.Count}.",
                    RelatedEventId: null,
                    ObligationId: inst.ObligationId,
                    RuleKey: null,
                    Severity: "warning",
                    Tags: new Dictionary<string, string>
                    {
                        ["trace_json"] = JsonSerializer.Serialize(trace)
                    }
                ));
            }
        }

        // Net charges by subtracting any ChargeAllocated events already applied in base projector.
        var netted = new List<ComputedCharge>();

        foreach (var c in state.Charges)
        {
            if (state.ChargePayments.TryGetValue(c.ChargeId, out var paid))
            {
                var remaining = c.Amount.Subtract(paid);
                if (remaining.Amount <= 0m) continue; // fully paid, drop it

                netted.Add(c with { Amount = remaining });
            }
            else
            {
                netted.Add(c);
            }
        }

        state.Charges.Clear();
        state.Charges.AddRange(netted);

        return state;
    }
    public async Task<FinancialState> ReplayAsync(
    IEnumerable<ProjectedEvent> events,
    IReadOnlyList<ExpectedInstallment> expectedInstallments,
    ProjectionContext context,
    CancellationToken ct)
    {
        var state = _baseProjector.Replay(events, expectedInstallments, context);

        var currency = context.BaseCurrency;

        foreach (var inst in state.Installments)
        {
            if (inst.IsFullyPaid) continue;

            var ctx = _ctxBuilder.BuildForInstallment(inst, context.AsOfDate, currency.Code);
            var (effects, trace) = await _ruleEngine.EvaluateAsync(ctx, ct);

            var charges = _mapper.Map(ctx, effects, trace, currency);
            state.Charges.AddRange(charges);

            // deterministic audit for rules uses projection context (we don't yet have per-rule occurredAt)
            foreach (var charge in charges)
            {
                state.Audit.Add(new DebtManager.Domain.Audit.AuditEntry(
                    At: DateTimeOffset.UtcNow,
                    EffectiveDate: charge.EffectiveDate,
                    Category: "RuleEngine",
                    Message: $"Charge applied: {charge.Label} {charge.Amount} on installment due {inst.DueDate:yyyy-MM-dd}. Effects={effects.Count}.",
                    RelatedEventId: null,
                    ObligationId: inst.ObligationId,
                    RuleKey: null,
                    Severity: "warning",
                    Tags: new Dictionary<string, string>
                    {
                        ["trace_json"] = JsonSerializer.Serialize(trace)
                    }
                ));
            }
        }

        return state;
    }
}
