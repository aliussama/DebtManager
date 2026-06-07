using DebtManager.Domain.Projections.Charges;
using DebtManager.Domain.Rules;
using DebtManager.Domain.Services.Finance;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Services.Rules;

public sealed class RuleEffectMapper
{
    // Facts keys (v1 standard)
    private const string FactInstallmentDueDate = "installment_due_date";   // DateOnly or "yyyy-MM-dd"
    private const string FactOutstandingAmount = "outstanding_amount";      // decimal (amount only)
    private const string FactOutstandingCurrency = "outstanding_currency";  // string (e.g. "EGP") optional

    public IEnumerable<ComputedCharge> Map(
        RuleEvaluationContext ctx,
        IReadOnlyList<RuleEffect> effects,
        RuleTrace trace,
        Currency currency)
    {
        foreach (var effect in effects)
        {
            // -------------------------
            // 1) Interest accrual effect (computed amount)
            // -------------------------
            if (string.Equals(effect.EffectType, RuleEffectTypes.InterestAccrual, StringComparison.OrdinalIgnoreCase))
            {
                // Required: rate
                if (!effect.Data.TryGetValue(RuleEffectFields.Rate, out var rateObj)) continue;
                var rate = Convert.ToDecimal(rateObj);

                // Optional: label
                var interestLabel = effect.Data.TryGetValue(RuleEffectFields.Label, out var lObj)
                    ? (Convert.ToString(lObj) ?? "Interest")
                    : "Interest";

                // Optional: compounding / basis
                var compStr = effect.Data.TryGetValue(RuleEffectFields.Compounding, out var cObj)
                    ? (Convert.ToString(cObj) ?? "daily")
                    : "daily";

                var basisStr = effect.Data.TryGetValue(RuleEffectFields.Basis, out var bObj)
                    ? (Convert.ToString(bObj) ?? "actual365")
                    : "actual365";

                var comp = compStr.ToLowerInvariant() switch
                {
                    "simple" => Compounding.Simple,
                    "monthly" => Compounding.Monthly,
                    _ => Compounding.Daily
                };

                var basis = basisStr.ToLowerInvariant() switch
                {
                    "actual360" => DayCountBasis.Actual360,
                    "30e360" => DayCountBasis.ThirtyE360,
                    _ => DayCountBasis.Actual365
                };

                // Required facts: due date + outstanding amount
                if (!TryGetFactDate(ctx.Facts, FactInstallmentDueDate, out var dueDate)) continue;
                if (!TryGetFactDecimal(ctx.Facts, FactOutstandingAmount, out var outstandingAmount)) continue;

                // Currency for outstanding: prefer fact, else ctx.CurrencyCode, else mapper currency
                var outCode =
                    TryGetFactString(ctx.Facts, FactOutstandingCurrency, out var c1) ? c1 :
                    (!string.IsNullOrWhiteSpace(ctx.CurrencyCode) ? ctx.CurrencyCode : currency.Code);

                var outCurrency = ResolveCurrency(outCode, currency);

                var principal = new Money(outstandingAmount, outCurrency);

                // Accrual window: from due date to evaluation date (inclusive)
                var from = dueDate;
                var toExclusive = ctx.EvaluationDate.AddDays(1);

                var interestMoney = InterestCalculator.Accrue(principal, rate, from, toExclusive, basis, comp);
                if (interestMoney.Amount <= 0m) continue;

                var interestRuleKey = effect.Data.TryGetValue(RuleEffectFields.RuleKey, out var rkObj1)
                    ? (Convert.ToString(rkObj1) ?? "unknown")
                    : (trace.FiredRuleKeys.FirstOrDefault() ?? "unknown");

                yield return new ComputedCharge(
                    ChargeId: Guid.NewGuid(),
                    ObligationId: ctx.ObligationId,
                    InstallmentKey: ctx.InstallmentKey,
                    Type: ChargeType.Interest,
                    Amount: new Money(interestMoney.Amount, currency),
                    EffectiveDate: ctx.EvaluationDate,
                    Label: interestLabel,
                    RuleKey: interestRuleKey,
                    RulePackVersionId: trace.VersionId.Value
                );

                continue;
            }

            // -------------------------
            // 2) Generic charge effect (amount provided)
            // -------------------------
            if (!effect.Data.TryGetValue(RuleEffectFields.Amount, out var amountObj)) continue;
            if (!effect.Data.TryGetValue(RuleEffectFields.Label, out var labelObj)) continue;

            var amount = Convert.ToDecimal(amountObj);
            var chargeLabel = Convert.ToString(labelObj) ?? "Charge";

            var chargeType = ChargeType.Other;
            if (effect.Data.TryGetValue(RuleEffectFields.ChargeType, out var ctObj))
            {
                var s = Convert.ToString(ctObj) ?? "";
                chargeType = s.ToLowerInvariant() switch
                {
                    "interest" => ChargeType.Interest,
                    "penalty" => ChargeType.Penalty,
                    "fee" => ChargeType.Fee,
                    "tax" => ChargeType.Tax,
                    _ => ChargeType.Other
                };
            }

            var chargeRuleKey = effect.Data.TryGetValue(RuleEffectFields.RuleKey, out var rkObj2)
                ? (Convert.ToString(rkObj2) ?? "unknown")
                : (trace.FiredRuleKeys.FirstOrDefault() ?? "unknown");

            yield return new ComputedCharge(
                ChargeId: Guid.NewGuid(),
                ObligationId: ctx.ObligationId,
                InstallmentKey: ctx.InstallmentKey,
                Type: chargeType,
                Amount: new Money(amount, currency),
                EffectiveDate: ctx.EvaluationDate,
                Label: chargeLabel,
                RuleKey: chargeRuleKey,
                RulePackVersionId: trace.VersionId.Value
            );
        }
    }

    private static bool TryGetFactDecimal(IReadOnlyDictionary<string, object> facts, string key, out decimal value)
    {
        value = 0m;
        if (!facts.TryGetValue(key, out var obj) || obj is null) return false;

        try
        {
            value = Convert.ToDecimal(obj);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetFactString(IReadOnlyDictionary<string, object> facts, string key, out string value)
    {
        value = "";
        if (!facts.TryGetValue(key, out var obj) || obj is null) return false;

        value = Convert.ToString(obj) ?? "";
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetFactDate(IReadOnlyDictionary<string, object> facts, string key, out DateOnly date)
    {
        date = default;
        if (!facts.TryGetValue(key, out var obj) || obj is null) return false;

        if (obj is DateOnly d)
        {
            date = d;
            return true;
        }

        var s = Convert.ToString(obj);
        if (string.IsNullOrWhiteSpace(s)) return false;

        return DateOnly.TryParse(s, out date);
    }

    private static Currency ResolveCurrency(string code, Currency fallback)
    {
        // v1: keep it simple and stable
        return code switch
        {
            "EGP" => Currency.EGP,
            "USD" => Currency.USD,
            "EUR" => Currency.EUR,
            _ => fallback
        };
    }
}
