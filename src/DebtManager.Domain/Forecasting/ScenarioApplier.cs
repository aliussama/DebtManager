using System.Text.Json;
using DebtManager.Domain.Fx;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Forecasting;

/// <summary>
/// Pure deterministic: converts ScenarioDefinition changes into ForecastAdjustments.
/// No side effects, no event writes.
/// </summary>
public static class ScenarioApplier
{
    public static IReadOnlyList<ForecastAdjustment> Apply(ScenarioDefinition scenario)
    {
        var adjustments = new List<ForecastAdjustment>();

        foreach (var change in scenario.Changes)
        {
            switch (change.Kind)
            {
                case ScenarioChangeKind.OneTimeIncome:
                    adjustments.Add(ParseOneTime(change, "Income"));
                    break;
                case ScenarioChangeKind.OneTimeExpense:
                    adjustments.Add(ParseOneTime(change, "Expense"));
                    break;
                case ScenarioChangeKind.OneTimeTransfer:
                    adjustments.AddRange(ParseTransfer(change));
                    break;
                case ScenarioChangeKind.DebtExtraPayment:
                    adjustments.Add(ParseOneTime(change, "DebtPayment"));
                    break;
                case ScenarioChangeKind.PauseRecurring:
                    adjustments.Add(ParsePauseRecurring(change));
                    break;
                case ScenarioChangeKind.BudgetOverride:
                case ScenarioChangeKind.RecurringOverride:
                case ScenarioChangeKind.FxPolicyOverride:
                case ScenarioChangeKind.ReportingCurrencyOverride:
                    // These are handled at a higher level (config overrides, not cashflow adjustments)
                    break;
            }
        }

        return adjustments;
    }

    /// <summary>
    /// Extract FxPolicyConfig override from scenario if any.
    /// </summary>
    public static FxPolicyConfig? ExtractFxPolicyOverride(ScenarioDefinition scenario)
    {
        var change = scenario.Changes.LastOrDefault(c => c.Kind == ScenarioChangeKind.FxPolicyOverride);
        if (change == null) return null;

        try
        {
            using var doc = JsonDocument.Parse(change.PayloadJson);
            var root = doc.RootElement;
            var policy = Enum.Parse<FxValuationPolicy>(root.GetProperty("policy").GetString() ?? "NearestBefore");
            var maxAge = root.GetProperty("maxAgeDays").GetInt32();
            return new FxPolicyConfig(policy, maxAge);
        }
        catch { return null; }
    }

    /// <summary>
    /// Extract reporting currency override from scenario if any.
    /// </summary>
    public static string? ExtractReportingCurrencyOverride(ScenarioDefinition scenario)
    {
        var change = scenario.Changes.LastOrDefault(c => c.Kind == ScenarioChangeKind.ReportingCurrencyOverride);
        if (change == null) return null;

        try
        {
            using var doc = JsonDocument.Parse(change.PayloadJson);
            return doc.RootElement.GetProperty("currencyCode").GetString();
        }
        catch { return null; }
    }

    private static ForecastAdjustment ParseOneTime(ScenarioChange change, string kind)
    {
        using var doc = JsonDocument.Parse(change.PayloadJson);
        var root = doc.RootElement;

        return new ForecastAdjustment(
            Date: DateOnly.Parse(root.GetProperty("date").GetString()!),
            AccountId: root.TryGetProperty("accountId", out var aid) && aid.ValueKind != JsonValueKind.Null
                ? Guid.Parse(aid.GetString()!) : null,
            CurrencyCode: root.GetProperty("currencyCode").GetString()!,
            Amount: root.GetProperty("amount").GetDecimal(),
            Kind: kind,
            Category: root.TryGetProperty("category", out var cat) ? cat.GetString() ?? string.Empty : string.Empty,
            Reference: root.TryGetProperty("reference", out var r) ? r.GetString() ?? string.Empty : string.Empty,
            RecurringIdToSuppress: null);
    }

    private static IReadOnlyList<ForecastAdjustment> ParseTransfer(ScenarioChange change)
    {
        using var doc = JsonDocument.Parse(change.PayloadJson);
        var root = doc.RootElement;

        var date = DateOnly.Parse(root.GetProperty("date").GetString()!);
        var amount = root.GetProperty("amount").GetDecimal();
        var currency = root.GetProperty("currencyCode").GetString()!;
        var fromAccount = Guid.Parse(root.GetProperty("fromAccountId").GetString()!);
        var toAccount = Guid.Parse(root.GetProperty("toAccountId").GetString()!);

        return new[]
        {
            new ForecastAdjustment(date, fromAccount, currency, -amount, "Transfer", string.Empty, "Scenario Transfer", null),
            new ForecastAdjustment(date, toAccount, currency, amount, "Transfer", string.Empty, "Scenario Transfer", null)
        };
    }

    private static ForecastAdjustment ParsePauseRecurring(ScenarioChange change)
    {
        using var doc = JsonDocument.Parse(change.PayloadJson);
        var root = doc.RootElement;
        var recurringId = Guid.Parse(root.GetProperty("recurringId").GetString()!);

        return new ForecastAdjustment(
            Date: default,
            AccountId: null,
            CurrencyCode: string.Empty,
            Amount: 0m,
            Kind: "Suppress",
            Category: string.Empty,
            Reference: string.Empty,
            RecurringIdToSuppress: recurringId);
    }
}
