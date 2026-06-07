using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DebtManager.Domain.Billing;
using DebtManager.Domain.Projections;

namespace DebtManager.Domain.Notifications;

/// <summary>
/// Pure deterministic notification candidate generator.
/// No side effects, no IO, no DateTime.Now.
/// </summary>
public static class NotificationRuleEngine
{
    public static IReadOnlyList<NotificationCandidate> BuildCandidates(
        DateOnly asOfDate,
        BillingState billingState,
        ContractsState contractsState,
        BudgetState budgetState,
        CashLedgerState cashLedgerState,
        RecurringState recurringState,
        TaxState taxState,
        CategoryState categoryState,
        IReadOnlyList<NotificationRuleRecord> activeRules)
    {
        var candidates = new List<NotificationCandidate>();

        foreach (var rule in activeRules)
        {
            if (!rule.IsEnabled || rule.IsArchived) continue;

            var config = ParseConfig(rule.ConfigJson);
            var daysBefore = config.DaysBefore ?? 3;
            var threshold = config.Threshold ?? 80m;

            switch (rule.RuleCode)
            {
                case "REM-BILL-DUE":
                    candidates.AddRange(GenerateBillDueReminders(asOfDate, billingState, rule, daysBefore));
                    break;
                case "REM-BILL-OVERDUE":
                    candidates.AddRange(GenerateBillOverdueReminders(asOfDate, billingState, rule));
                    break;
                case "REM-INV-DUE":
                    candidates.AddRange(GenerateInvoiceDueReminders(asOfDate, billingState, rule, daysBefore));
                    break;
                case "REM-INV-OVERDUE":
                    candidates.AddRange(GenerateInvoiceOverdueReminders(asOfDate, billingState, rule));
                    break;
                case "REM-CONTRACT-ENDING":
                    candidates.AddRange(GenerateContractEndingReminders(asOfDate, contractsState, rule, daysBefore));
                    break;
                case "REM-BUDGET-THRESHOLD":
                    candidates.AddRange(GenerateBudgetThresholdWarnings(asOfDate, budgetState, cashLedgerState, categoryState, rule, threshold));
                    break;
                case "REM-FORECAST-NEGATIVE":
                    candidates.AddRange(GenerateForecastNegativeWarnings(asOfDate, cashLedgerState, rule));
                    break;
                case "REM-RECURRING-ANOMALY":
                    candidates.AddRange(GenerateRecurringAnomalies(asOfDate, recurringState, cashLedgerState, rule));
                    break;
                case "REM-TAX-DEADLINE":
                    candidates.AddRange(GenerateTaxDeadlineReminders(asOfDate, taxState, rule, daysBefore));
                    break;
            }
        }

        return candidates;
    }

    private static IEnumerable<NotificationCandidate> GenerateBillDueReminders(
        DateOnly asOfDate, BillingState billing, NotificationRuleRecord rule, int daysBefore)
    {
        var threshold = asOfDate.AddDays(daysBefore);
        foreach (var bill in billing.Bills.Values)
        {
            if (bill.Status is "Cancelled" or "Paid" or "WrittenOff") continue;
            if (bill.DueDate > asOfDate && bill.DueDate <= threshold)
            {
                var dedupKey = ComputeDedupKey(rule.RuleCode, bill.BillId.ToString(), bill.DueDate.ToString("yyyy-MM-dd"));
                yield return new NotificationCandidate(
                    DeterministicGuid(dedupKey),
                    rule.RuleCode, rule.Area, rule.Severity,
                    $"Bill due in {(bill.DueDate.DayNumber - asOfDate.DayNumber)} day(s)",
                    $"Bill {bill.Reference} for {bill.Amount:N2} {bill.CurrencyCode} is due on {bill.DueDate:yyyy-MM-dd}. Outstanding: {bill.Outstanding:N2}",
                    asOfDate, bill.DueDate,
                    JsonSerializer.Serialize(new { BillId = bill.BillId }),
                    dedupKey);
            }
        }
    }

    private static IEnumerable<NotificationCandidate> GenerateBillOverdueReminders(
        DateOnly asOfDate, BillingState billing, NotificationRuleRecord rule)
    {
        foreach (var bill in billing.Bills.Values)
        {
            if (bill.Status is "Cancelled" or "Paid" or "WrittenOff") continue;
            if (bill.DueDate < asOfDate && bill.Outstanding > 0)
            {
                var daysOverdue = asOfDate.DayNumber - bill.DueDate.DayNumber;
                var dedupKey = ComputeDedupKey(rule.RuleCode, bill.BillId.ToString(), bill.DueDate.ToString("yyyy-MM-dd"));
                yield return new NotificationCandidate(
                    DeterministicGuid(dedupKey),
                    rule.RuleCode, rule.Area, "Error",
                    $"Bill overdue by {daysOverdue} day(s)",
                    $"Bill {bill.Reference} for {bill.Amount:N2} {bill.CurrencyCode} was due {bill.DueDate:yyyy-MM-dd}. Outstanding: {bill.Outstanding:N2}",
                    asOfDate, bill.DueDate,
                    JsonSerializer.Serialize(new { BillId = bill.BillId }),
                    dedupKey);
            }
        }
    }

    private static IEnumerable<NotificationCandidate> GenerateInvoiceDueReminders(
        DateOnly asOfDate, BillingState billing, NotificationRuleRecord rule, int daysBefore)
    {
        var threshold = asOfDate.AddDays(daysBefore);
        foreach (var inv in billing.Invoices.Values)
        {
            if (inv.Status is "Cancelled" or "Paid" or "WrittenOff") continue;
            if (inv.DueDate > asOfDate && inv.DueDate <= threshold)
            {
                var dedupKey = ComputeDedupKey(rule.RuleCode, inv.InvoiceId.ToString(), inv.DueDate.ToString("yyyy-MM-dd"));
                yield return new NotificationCandidate(
                    DeterministicGuid(dedupKey),
                    rule.RuleCode, rule.Area, rule.Severity,
                    $"Invoice due in {(inv.DueDate.DayNumber - asOfDate.DayNumber)} day(s)",
                    $"Invoice {inv.Reference} for {inv.Amount:N2} {inv.CurrencyCode} is due on {inv.DueDate:yyyy-MM-dd}. Outstanding: {inv.Outstanding:N2}",
                    asOfDate, inv.DueDate,
                    JsonSerializer.Serialize(new { InvoiceId = inv.InvoiceId }),
                    dedupKey);
            }
        }
    }

    private static IEnumerable<NotificationCandidate> GenerateInvoiceOverdueReminders(
        DateOnly asOfDate, BillingState billing, NotificationRuleRecord rule)
    {
        foreach (var inv in billing.Invoices.Values)
        {
            if (inv.Status is "Cancelled" or "Paid" or "WrittenOff") continue;
            if (inv.DueDate < asOfDate && inv.Outstanding > 0)
            {
                var daysOverdue = asOfDate.DayNumber - inv.DueDate.DayNumber;
                var dedupKey = ComputeDedupKey(rule.RuleCode, inv.InvoiceId.ToString(), inv.DueDate.ToString("yyyy-MM-dd"));
                yield return new NotificationCandidate(
                    DeterministicGuid(dedupKey),
                    rule.RuleCode, rule.Area, "Error",
                    $"Invoice overdue by {daysOverdue} day(s)",
                    $"Invoice {inv.Reference} for {inv.Amount:N2} {inv.CurrencyCode} was due {inv.DueDate:yyyy-MM-dd}. Outstanding: {inv.Outstanding:N2}",
                    asOfDate, inv.DueDate,
                    JsonSerializer.Serialize(new { InvoiceId = inv.InvoiceId }),
                    dedupKey);
            }
        }
    }

    private static IEnumerable<NotificationCandidate> GenerateContractEndingReminders(
        DateOnly asOfDate, ContractsState contracts, NotificationRuleRecord rule, int daysBefore)
    {
        var threshold = asOfDate.AddDays(daysBefore);
        foreach (var c in contracts.Contracts.Values)
        {
            if (c.IsArchived || !c.EndDate.HasValue) continue;
            if (c.EndDate.Value > asOfDate && c.EndDate.Value <= threshold)
            {
                var daysLeft = c.EndDate.Value.DayNumber - asOfDate.DayNumber;
                var dedupKey = ComputeDedupKey(rule.RuleCode, c.ContractId.ToString(), c.EndDate.Value.ToString("yyyy-MM-dd"));
                yield return new NotificationCandidate(
                    DeterministicGuid(dedupKey),
                    rule.RuleCode, rule.Area, rule.Severity,
                    $"Contract ending in {daysLeft} day(s)",
                    $"Contract '{c.Title}' ({c.ContractType}) ends on {c.EndDate.Value:yyyy-MM-dd}.",
                    asOfDate, c.EndDate.Value,
                    JsonSerializer.Serialize(new { ContractId = c.ContractId }),
                    dedupKey);
            }
        }
    }

    private static IEnumerable<NotificationCandidate> GenerateBudgetThresholdWarnings(
        DateOnly asOfDate, BudgetState budgetState, CashLedgerState cashState,
        CategoryState categoryState, NotificationRuleRecord rule, decimal threshold)
    {
        var utilization = BudgetProjector.ComputeUtilization(
            budgetState, cashState, categoryState, asOfDate.Year, asOfDate.Month);
        foreach (var row in utilization)
        {
            if (row.PercentUsed >= threshold)
            {
                var dedupKey = ComputeDedupKey(rule.RuleCode, row.BudgetId.ToString(), $"{asOfDate:yyyy-MM}");
                var severity = row.PercentUsed >= 100 ? "Error" : "Warning";
                yield return new NotificationCandidate(
                    DeterministicGuid(dedupKey),
                    rule.RuleCode, "Budgets", severity,
                    $"Budget {row.ScopeLabel}: {row.PercentUsed:N0}% used",
                    $"Budget '{row.ScopeLabel}' has used {row.ActualAmount:N2}/{row.LimitAmount:N2} {row.CurrencyCode} ({row.PercentUsed:N0}%).",
                    asOfDate, null,
                    JsonSerializer.Serialize(new { BudgetId = row.BudgetId }),
                    dedupKey);
            }
        }
    }

    private static IEnumerable<NotificationCandidate> GenerateForecastNegativeWarnings(
        DateOnly asOfDate, CashLedgerState cashState, NotificationRuleRecord rule)
    {
        foreach (var account in cashState.Accounts.Values)
        {
            if (account.IsArchived) continue;
            if (account.Balance < 0)
            {
                var dedupKey = ComputeDedupKey(rule.RuleCode, account.AccountId.ToString(), $"{asOfDate:yyyy-MM}");
                yield return new NotificationCandidate(
                    DeterministicGuid(dedupKey),
                    rule.RuleCode, "Forecast", "Warning",
                    $"Negative balance: {account.Name}",
                    $"Account '{account.Name}' has a negative balance of {account.Balance:N2} {account.CurrencyCode}.",
                    asOfDate, null,
                    JsonSerializer.Serialize(new { AccountId = account.AccountId }),
                    dedupKey);
            }
        }
    }

    private static IEnumerable<NotificationCandidate> GenerateRecurringAnomalies(
        DateOnly asOfDate, RecurringState recurringState, CashLedgerState cashState,
        NotificationRuleRecord rule)
    {
        foreach (var item in recurringState.Items.Values)
        {
            if (item.IsArchived) continue;
            var lastPosted = item.PostedDates.Count > 0 ? item.PostedDates.Max() : (DateOnly?)null;
            if (lastPosted.HasValue)
            {
                var daysSincePost = asOfDate.DayNumber - lastPosted.Value.DayNumber;
                var expectedInterval = item.Frequency switch
                {
                    "Daily" => 1,
                    "Weekly" => 7,
                    "Monthly" => 35,
                    "Quarterly" => 100,
                    "Yearly" => 380,
                    _ => 35
                };

                if (daysSincePost > expectedInterval * 1.5)
                {
                    var label = item.Notes ?? item.Reference ?? item.RecurringId.ToString()[..8];
                    var dedupKey = ComputeDedupKey(rule.RuleCode, item.RecurringId.ToString(), $"missing-{asOfDate:yyyy-MM}");
                    yield return new NotificationCandidate(
                        DeterministicGuid(dedupKey),
                        rule.RuleCode, "Recurring", "Warning",
                        $"Recurring transaction may be missing",
                        $"'{label}' (every {item.Frequency}) was last posted {lastPosted.Value:yyyy-MM-dd} — {daysSincePost} days ago.",
                        asOfDate, null,
                        JsonSerializer.Serialize(new { RecurringId = item.RecurringId }),
                        dedupKey);
                }
            }
        }
    }

    private static IEnumerable<NotificationCandidate> GenerateTaxDeadlineReminders(
        DateOnly asOfDate, TaxState taxState, NotificationRuleRecord rule, int daysBefore)
    {
        foreach (var profile in taxState.Profiles.Values)
        {
            if (profile.IsArchived) continue;
            var deadlineMonth = profile.TaxYearStartMonth == 1 ? 12 : profile.TaxYearStartMonth - 1;
            var deadlineDay = Math.Min(profile.TaxYearStartDay, DateTime.DaysInMonth(asOfDate.Year, deadlineMonth));
            var deadlineYear = profile.TaxYearStartMonth == 1 ? asOfDate.Year : asOfDate.Year;
            var deadline = new DateOnly(deadlineYear, deadlineMonth, deadlineDay);

            if (deadline < asOfDate)
                deadline = deadline.AddYears(1);

            var daysLeft = deadline.DayNumber - asOfDate.DayNumber;
            if (daysLeft >= 0 && daysLeft <= daysBefore)
            {
                var dedupKey = ComputeDedupKey(rule.RuleCode, profile.ProfileId.ToString(), $"{deadline:yyyy-MM-dd}");
                yield return new NotificationCandidate(
                    DeterministicGuid(dedupKey),
                    rule.RuleCode, "Taxes", rule.Severity,
                    $"Tax year deadline in {daysLeft} day(s)",
                    $"Tax profile '{profile.Name}' ({profile.CountryCode}) year-end deadline is {deadline:yyyy-MM-dd}.",
                    asOfDate, deadline,
                    JsonSerializer.Serialize(new { ProfileId = profile.ProfileId }),
                    dedupKey);
            }
        }
    }

    // --- Deterministic helpers ---

    public static string ComputeDedupKey(string ruleCode, string refId, string bucket)
    {
        var input = $"{ruleCode}|{refId}|{bucket}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }

    public static Guid DeterministicGuid(string dedupKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(dedupKey));
        return new Guid(hash.AsSpan()[..16]);
    }

    private static RuleConfig ParseConfig(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<RuleConfig>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new RuleConfig();
        }
        catch { return new RuleConfig(); }
    }

    private sealed class RuleConfig
    {
        public int? DaysBefore { get; set; }
        public decimal? Threshold { get; set; }
    }
}
