using System.Text.Json;

namespace DebtManager.Domain.Billing;

/// <summary>
/// Terms JSON model for contract billing generation.
/// </summary>
public sealed class ContractTerms
{
    public string BillingCycle { get; set; } = "Monthly"; // Weekly, Monthly, Quarterly, Yearly
    public int BillingInterval { get; set; } = 1;
    public int BillingDayOfMonth { get; set; } = 1;
    public decimal BaseAmount { get; set; }
    public string? Category { get; set; }
    public decimal AnnualEscalationPercent { get; set; }
    public int GracePeriodDays { get; set; }
    public decimal? LateFeeAmount { get; set; }
    public string? CancellationPolicy { get; set; }
}

/// <summary>
/// Represents a candidate billing item to be generated from a contract.
/// </summary>
public sealed record ContractBillingCandidate(
    Guid ContractId,
    string CycleKey,
    DateOnly DueDate,
    decimal Amount,
    string CurrencyCode,
    string Category,
    string Reference
);

/// <summary>
/// Deterministic generation of bill/invoice cycles from contract TermsJson.
/// Pure computation — no side effects.
/// </summary>
public static class ContractBillingGenerator
{
    /// <summary>
    /// Generate candidate billing items for a contract between startDate and asOfDate.
    /// CycleKey format ensures idempotency.
    /// </summary>
    public static IReadOnlyList<ContractBillingCandidate> GenerateCandidates(
        Guid contractId,
        DateOnly contractStartDate,
        DateOnly? contractEndDate,
        string currencyCode,
        string termsJson,
        DateOnly asOfDate,
        IReadOnlySet<string>? alreadyGeneratedCycleKeys = null)
    {
        var terms = ParseTerms(termsJson);
        var candidates = new List<ContractBillingCandidate>();
        var generated = alreadyGeneratedCycleKeys ?? new HashSet<string>();

        var current = ComputeFirstBillingDate(contractStartDate, terms);
        var yearsSinceStart = 0;
        var lastEscalationYear = contractStartDate.Year;

        while (current <= asOfDate)
        {
            if (contractEndDate.HasValue && current > contractEndDate.Value)
                break;

            var cycleKey = FormatCycleKey(current, terms.BillingCycle);

            if (!generated.Contains(cycleKey))
            {
                // Apply escalation
                var escalationYears = current.Year - contractStartDate.Year;
                var amount = terms.BaseAmount;
                if (terms.AnnualEscalationPercent > 0 && escalationYears > 0)
                {
                    for (int y = 0; y < escalationYears; y++)
                        amount *= (1 + terms.AnnualEscalationPercent / 100m);
                    amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
                }

                var reference = $"Contract-{contractId:N}-{cycleKey}";
                var category = terms.Category ?? "Contract";

                candidates.Add(new ContractBillingCandidate(
                    contractId, cycleKey, current, amount, currencyCode, category, reference));
            }

            current = AdvanceBillingDate(current, terms);
        }

        return candidates;
    }

    private static ContractTerms ParseTerms(string termsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<ContractTerms>(termsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ContractTerms();
        }
        catch
        {
            return new ContractTerms();
        }
    }

    private static DateOnly ComputeFirstBillingDate(DateOnly contractStart, ContractTerms terms)
    {
        var day = Math.Min(terms.BillingDayOfMonth, DateTime.DaysInMonth(contractStart.Year, contractStart.Month));
        var first = new DateOnly(contractStart.Year, contractStart.Month, day);
        if (first < contractStart)
            first = AdvanceBillingDate(first, terms);
        return first;
    }

    private static DateOnly AdvanceBillingDate(DateOnly date, ContractTerms terms)
    {
        return terms.BillingCycle switch
        {
            "Weekly" => date.AddDays(7 * terms.BillingInterval),
            "Monthly" => date.AddMonths(terms.BillingInterval),
            "Quarterly" => date.AddMonths(3 * terms.BillingInterval),
            "Yearly" => date.AddYears(terms.BillingInterval),
            _ => date.AddMonths(terms.BillingInterval)
        };
    }

    private static string FormatCycleKey(DateOnly date, string cycle)
    {
        return cycle switch
        {
            "Weekly" => $"W-{date:yyyy-MM-dd}",
            "Yearly" => $"Y-{date:yyyy}",
            "Quarterly" => $"Q-{date:yyyy}-Q{((date.Month - 1) / 3) + 1}",
            _ => $"M-{date:yyyy-MM}"
        };
    }
}
