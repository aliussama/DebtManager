using DebtManager.Domain.Projections.Charges;
using DebtManager.Domain.Rules;
using DebtManager.Domain.Services.Finance;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Services.Rules;

/// <summary>
/// Rule engine wrapper that produces detailed calculation traces.
/// Every evaluation is explainable and auditable.
/// </summary>
public sealed class TracingRuleEngine
{
    private readonly IRuleEngine _innerEngine;

    public TracingRuleEngine(IRuleEngine innerEngine)
    {
        _innerEngine = innerEngine;
    }

    /// <summary>
    /// Evaluate rules with full trace generation.
    /// </summary>
    public async Task<TracedEvaluationResult> EvaluateWithTraceAsync(
        RuleEvaluationContext ctx,
        CancellationToken ct = default)
    {
        var traceBuilder = CalculationTrace.CreateBuilder(
            ctx.ObligationId,
            ctx.EvaluationDate,
            ctx.InstallmentKey?.ToString()
        );

        // Capture input facts
        traceBuilder.AddInputFacts(ctx.Facts);

        // Evaluate rules using existing interface (returns tuple)
        var (effects, trace) = await _innerEngine.EvaluateAsync(ctx, ct);

        // Process grace period effects first
        var effectiveCtx = ApplyGracePeriodEffects(ctx, effects, traceBuilder);

        // Generate charges with formula breakdowns
        var charges = new List<ComputedCharge>();

        foreach (var effect in effects)
        {
            var charge = ProcessEffectWithTrace(effectiveCtx, effect, trace, traceBuilder);
            if (charge != null)
            {
                charges.Add(charge);
            }
        }

        // Record fired rules
        foreach (var ruleKey in trace.FiredRuleKeys)
        {
            traceBuilder.AddRuleFired(
                ruleKey: ruleKey,
                rulePackId: trace.VersionId.Value.ToString(),
                phase: "evaluated",
                effectiveFrom: ctx.EvaluationDate,
                effectiveTo: null,
                effectTypes: effects.Select(e => e.EffectType).ToList()
            );
        }

        var calculationTrace = traceBuilder.Build();

        return new TracedEvaluationResult(
            Charges: charges.AsReadOnly(),
            Trace: calculationTrace,
            Effects: effects,
            InnerTrace: trace
        );
    }

    private RuleEvaluationContext ApplyGracePeriodEffects(
        RuleEvaluationContext ctx,
        IReadOnlyList<RuleEffect> effects,
        CalculationTraceBuilder traceBuilder)
    {
        var graceEffects = effects
            .Where(e => e.EffectType == RuleEffectTypes.ApplyGrace)
            .ToList();

        if (!graceEffects.Any())
            return ctx;

        var modifiedFacts = new Dictionary<string, object>(ctx.Facts);

        foreach (var effect in graceEffects)
        {
            if (!effect.Data.TryGetValue(RuleEffectFields.GraceDays, out var graceDaysObj))
                continue;

            var graceDays = Convert.ToInt32(graceDaysObj);

            // Check if currently within grace
            if (modifiedFacts.TryGetValue("installment.days_overdue", out var daysOverdueObj))
            {
                var daysOverdue = Convert.ToInt32(daysOverdueObj);
                var effectiveDaysOverdue = Math.Max(0, daysOverdue - graceDays);
                modifiedFacts["installment.effective_days_overdue"] = effectiveDaysOverdue;
                modifiedFacts["installment.within_grace"] = daysOverdue <= graceDays;

                traceBuilder.AddEffect(
                    effectType: RuleEffectTypes.ApplyGrace,
                    label: "Grace Period Applied",
                    amount: Money.Zero(Currency.EGP),
                    formula: $"EffectiveDaysOverdue = max(0, {daysOverdue} - {graceDays}) = {effectiveDaysOverdue}",
                    formulaInputs: new Dictionary<string, object>
                    {
                        ["daysOverdue"] = daysOverdue,
                        ["graceDays"] = graceDays
                    },
                    intermediateSteps: new[]
                    {
                        $"Original days overdue: {daysOverdue}",
                        $"Grace period: {graceDays} days",
                        $"Within grace: {daysOverdue <= graceDays}"
                    },
                    ruleKey: effect.Data.TryGetValue(RuleEffectFields.RuleKey, out var rk)
                        ? rk?.ToString() ?? "grace_rule"
                        : "grace_rule"
                );
            }
        }

        return ctx with { Facts = modifiedFacts };
    }

    private ComputedCharge? ProcessEffectWithTrace(
        RuleEvaluationContext ctx,
        RuleEffect effect,
        RuleTrace trace,
        CalculationTraceBuilder traceBuilder)
    {
        return effect.EffectType switch
        {
            RuleEffectTypes.AccrueInterest => ProcessInterestEffect(ctx, effect, trace, traceBuilder),
            RuleEffectTypes.AddCharge => ProcessChargeEffect(ctx, effect, trace, traceBuilder),
            RuleEffectTypes.ApplyPenalty => ProcessPenaltyEffect(ctx, effect, trace, traceBuilder),
            RuleEffectTypes.ApplyFee => ProcessFeeEffect(ctx, effect, trace, traceBuilder),
            RuleEffectTypes.ApplyTax => ProcessTaxEffect(ctx, effect, trace, traceBuilder),
            _ => null // Grace and scheduling effects don't produce charges directly
        };
    }

    private ComputedCharge? ProcessInterestEffect(
        RuleEvaluationContext ctx,
        RuleEffect effect,
        RuleTrace trace,
        CalculationTraceBuilder traceBuilder)
    {
        // Extract rate and calculation parameters
        if (!effect.Data.TryGetValue(RuleEffectFields.Rate, out var rateObj))
            return null;

        var rate = Convert.ToDecimal(rateObj);
        var label = effect.Data.TryGetValue(RuleEffectFields.Label, out var labelObj)
            ? labelObj?.ToString() ?? "Interest"
            : "Interest";

        var compounding = effect.Data.TryGetValue(RuleEffectFields.Compounding, out var compObj)
            ? ParseCompounding(compObj?.ToString())
            : Compounding.Daily;

        var basis = effect.Data.TryGetValue(RuleEffectFields.Basis, out var basisObj)
            ? ParseDayCountBasis(basisObj?.ToString())
            : DayCountBasis.Actual365;

        // Get principal from facts
        if (!ctx.Facts.TryGetValue("outstanding.amount", out var principalObj))
            return null;

        var principal = Convert.ToDecimal(principalObj);
        var currency = ResolveCurrency(ctx);

        // Get accrual period
        var periodStart = ctx.Facts.TryGetValue("installment.due_date", out var dueDateObj)
            ? DateOnly.Parse(dueDateObj.ToString()!)
            : ctx.EvaluationDate.AddDays(-30);

        var periodEnd = ctx.EvaluationDate;

        // Build interest rule and calculate
        var interestRule = new InterestAccrualRule(
            ruleKey: effect.Data.TryGetValue(RuleEffectFields.RuleKey, out var rkObj)
                ? rkObj?.ToString() ?? "interest_rule"
                : "interest_rule",
            rateSchedule: new[] { new RateScheduleEntry(rate, periodStart) },
            compoundingMethod: compounding,
            dayCountBasis: basis
        );

        var accrualResult = interestRule.Calculate(
            new Money(principal, currency),
            periodStart,
            periodEnd
        );

        // Add to trace
        traceBuilder.AddEffect(
            effectType: RuleEffectTypes.AccrueInterest,
            label: label,
            amount: accrualResult.Interest,
            formula: accrualResult.Formula,
            formulaInputs: new Dictionary<string, object>
            {
                ["principal"] = principal,
                ["rate"] = rate,
                ["periodStart"] = periodStart.ToString("yyyy-MM-dd"),
                ["periodEnd"] = periodEnd.ToString("yyyy-MM-dd"),
                ["daysAccrued"] = accrualResult.DaysAccrued,
                ["compounding"] = compounding.ToString(),
                ["basis"] = basis.ToString()
            },
            intermediateSteps: accrualResult.Breakdown
                .Take(5)
                .Select(d => $"{d.Date:yyyy-MM-dd}: {d.Principal:N2} × {d.DailyRate:P6} = {d.Interest:N4}")
                .Append(accrualResult.Breakdown.Count > 5 ? $"... ({accrualResult.Breakdown.Count - 5} more days)" : "")
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList(),
            ruleKey: interestRule.RuleKey
        );

        return new ComputedCharge(
            ChargeId: Guid.NewGuid(),
            ObligationId: ctx.ObligationId,
            InstallmentKey: ctx.InstallmentKey,
            Type: ChargeType.Interest,
            Amount: accrualResult.Interest,
            EffectiveDate: ctx.EvaluationDate,
            Label: label,
            RuleKey: interestRule.RuleKey,
            RulePackVersionId: trace.VersionId.Value
        );
    }

    private ComputedCharge? ProcessChargeEffect(
        RuleEvaluationContext ctx,
        RuleEffect effect,
        RuleTrace trace,
        CalculationTraceBuilder traceBuilder)
    {
        if (!effect.Data.TryGetValue(RuleEffectFields.Amount, out var amountObj))
            return null;

        var amount = Convert.ToDecimal(amountObj);
        var label = effect.Data.TryGetValue(RuleEffectFields.Label, out var labelObj)
            ? labelObj?.ToString() ?? "Charge"
            : "Charge";

        var chargeType = effect.Data.TryGetValue(RuleEffectFields.ChargeType, out var ctObj)
            ? ParseChargeType(ctObj?.ToString())
            : ChargeType.Other;

        var currency = ResolveCurrency(ctx);
        var money = new Money(amount, currency);

        traceBuilder.AddEffect(
            effectType: RuleEffectTypes.AddCharge,
            label: label,
            amount: money,
            formula: $"Fixed charge = {amount:N2} {currency.Code}",
            ruleKey: effect.Data.TryGetValue(RuleEffectFields.RuleKey, out var rkObj)
                ? rkObj?.ToString() ?? "charge_rule"
                : "charge_rule"
        );

        return new ComputedCharge(
            ChargeId: Guid.NewGuid(),
            ObligationId: ctx.ObligationId,
            InstallmentKey: ctx.InstallmentKey,
            Type: chargeType,
            Amount: money,
            EffectiveDate: ctx.EvaluationDate,
            Label: label,
            RuleKey: effect.Data.TryGetValue(RuleEffectFields.RuleKey, out var rk) ? rk?.ToString() ?? "" : "",
            RulePackVersionId: trace.VersionId.Value
        );
    }

    private ComputedCharge? ProcessPenaltyEffect(
        RuleEvaluationContext ctx,
        RuleEffect effect,
        RuleTrace trace,
        CalculationTraceBuilder traceBuilder)
    {
        // Check if within grace period (skip penalty)
        if (ctx.Facts.TryGetValue("installment.within_grace", out var withinGraceObj)
            && Convert.ToBoolean(withinGraceObj))
        {
            return null;
        }

        if (!effect.Data.TryGetValue(RuleEffectFields.Amount, out var amountObj))
            return null;

        var amount = Convert.ToDecimal(amountObj);
        var label = effect.Data.TryGetValue(RuleEffectFields.Label, out var labelObj)
            ? labelObj?.ToString() ?? "Late Penalty"
            : "Late Penalty";

        var currency = ResolveCurrency(ctx);

        traceBuilder.AddEffect(
            effectType: RuleEffectTypes.ApplyPenalty,
            label: label,
            amount: new Money(amount, currency),
            formula: $"Penalty = {amount:N2}",
            ruleKey: effect.Data.TryGetValue(RuleEffectFields.RuleKey, out var rkObj) ? rkObj?.ToString() ?? "" : ""
        );

        return new ComputedCharge(
            ChargeId: Guid.NewGuid(),
            ObligationId: ctx.ObligationId,
            InstallmentKey: ctx.InstallmentKey,
            Type: ChargeType.Penalty,
            Amount: new Money(amount, currency),
            EffectiveDate: ctx.EvaluationDate,
            Label: label,
            RuleKey: effect.Data.TryGetValue(RuleEffectFields.RuleKey, out var rk) ? rk?.ToString() ?? "" : "",
            RulePackVersionId: trace.VersionId.Value
        );
    }

    private ComputedCharge? ProcessFeeEffect(
        RuleEvaluationContext ctx,
        RuleEffect effect,
        RuleTrace trace,
        CalculationTraceBuilder traceBuilder)
    {
        if (!effect.Data.TryGetValue(RuleEffectFields.Amount, out var amountObj))
            return null;

        var amount = Convert.ToDecimal(amountObj);
        var label = effect.Data.TryGetValue(RuleEffectFields.Label, out var labelObj)
            ? labelObj?.ToString() ?? "Fee"
            : "Fee";

        var currency = ResolveCurrency(ctx);
        var money = new Money(amount, currency);

        traceBuilder.AddEffect(
            effectType: RuleEffectTypes.ApplyFee,
            label: label,
            amount: money,
            formula: $"Fixed fee = {amount:N2} {currency.Code}",
            ruleKey: effect.Data.TryGetValue(RuleEffectFields.RuleKey, out var rkObj) ? rkObj?.ToString() ?? "" : ""
        );

        return new ComputedCharge(
            ChargeId: Guid.NewGuid(),
            ObligationId: ctx.ObligationId,
            InstallmentKey: ctx.InstallmentKey,
            Type: ChargeType.Fee,
            Amount: money,
            EffectiveDate: ctx.EvaluationDate,
            Label: label,
            RuleKey: effect.Data.TryGetValue(RuleEffectFields.RuleKey, out var rk) ? rk?.ToString() ?? "" : "",
            RulePackVersionId: trace.VersionId.Value
        );
    }

    private ComputedCharge? ProcessTaxEffect(
        RuleEvaluationContext ctx,
        RuleEffect effect,
        RuleTrace trace,
        CalculationTraceBuilder traceBuilder)
    {
        if (!effect.Data.TryGetValue(RuleEffectFields.Amount, out var amountObj))
            return null;

        var amount = Convert.ToDecimal(amountObj);
        var label = effect.Data.TryGetValue(RuleEffectFields.Label, out var labelObj)
            ? labelObj?.ToString() ?? "Tax"
            : "Tax";

        var currency = ResolveCurrency(ctx);
        var money = new Money(amount, currency);

        traceBuilder.AddEffect(
            effectType: RuleEffectTypes.ApplyTax,
            label: label,
            amount: money,
            formula: $"Tax = {amount:N2} {currency.Code}",
            ruleKey: effect.Data.TryGetValue(RuleEffectFields.RuleKey, out var rkObj) ? rkObj?.ToString() ?? "" : ""
        );

        return new ComputedCharge(
            ChargeId: Guid.NewGuid(),
            ObligationId: ctx.ObligationId,
            InstallmentKey: ctx.InstallmentKey,
            Type: ChargeType.Tax,
            Amount: money,
            EffectiveDate: ctx.EvaluationDate,
            Label: label,
            RuleKey: effect.Data.TryGetValue(RuleEffectFields.RuleKey, out var rk) ? rk?.ToString() ?? "" : "",
            RulePackVersionId: trace.VersionId.Value
        );
    }

    private static Currency ResolveCurrency(RuleEvaluationContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(ctx.CurrencyCode))
        {
            return ctx.CurrencyCode.ToUpperInvariant() switch
            {
                "USD" => Currency.USD,
                "EUR" => Currency.EUR,
                _ => Currency.EGP
            };
        }
        return Currency.EGP;
    }

    private static Compounding ParseCompounding(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "simple" => Compounding.Simple,
            "monthly" => Compounding.Monthly,
            "daily" => Compounding.Daily,
            _ => Compounding.Daily
        };
    }

    private static DayCountBasis ParseDayCountBasis(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "actual360" or "actual/360" => DayCountBasis.Actual360,
            "30e360" or "30e/360" => DayCountBasis.ThirtyE360,
            "actual365" or "actual/365" => DayCountBasis.Actual365,
            _ => DayCountBasis.Actual365
        };
    }

    private static ChargeType ParseChargeType(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "interest" => ChargeType.Interest,
            "penalty" => ChargeType.Penalty,
            "fee" => ChargeType.Fee,
            "tax" => ChargeType.Tax,
            _ => ChargeType.Other
        };
    }
}

/// <summary>
/// Result of rule evaluation with full trace.
/// </summary>
public sealed record TracedEvaluationResult(
    IReadOnlyList<ComputedCharge> Charges,
    CalculationTrace Trace,
    IReadOnlyList<RuleEffect> Effects,
    RuleTrace InnerTrace
);
