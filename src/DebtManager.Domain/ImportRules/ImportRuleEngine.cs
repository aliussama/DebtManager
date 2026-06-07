using System.Text.Json;
using System.Text.RegularExpressions;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.ImportRules;

/// <summary>
/// Pure deterministic rule engine: evaluates import rules against imported transactions
/// and produces suggestions. No side effects, no IO.
/// </summary>
public static class ImportRuleEngine
{
    private static readonly JsonSerializerOptions _jsonOpt = DomainJson.Options;

    public static List<ImportSuggestion> Evaluate(
        ImportedTransaction txn,
        IReadOnlyList<ImportRuleRecord> activeRules,
        BillingState? billingState,
        CashLedgerState? ledgerState,
        CategoryState? categoryState)
    {
        var suggestions = new List<ImportSuggestion>();

        foreach (var rule in activeRules)
        {
            if (!rule.IsEnabled || rule.IsArchived)
                continue;

            ImportCondition? condition;
            try { condition = JsonSerializer.Deserialize<ImportCondition>(rule.MatchSpecJson, _jsonOpt); }
            catch { continue; }

            if (condition == null || !EvaluateCondition(condition, txn))
                continue;

            ImportRuleAction? action;
            try { action = JsonSerializer.Deserialize<ImportRuleAction>(rule.ActionSpecJson, _jsonOpt); }
            catch { continue; }

            if (action == null) continue;

            var suggestion = BuildSuggestion(txn, rule, action, billingState);
            if (suggestion != null)
                suggestions.Add(suggestion);
        }

        suggestions.Sort((a, b) =>
        {
            int cmp = b.Confidence.CompareTo(a.Confidence);
            if (cmp != 0) return cmp;
            return string.CompareOrdinal(a.DeterministicSuggestionId, b.DeterministicSuggestionId);
        });

        return suggestions;
    }

    // ????????????????????????????????????????????
    // Condition evaluation
    // ????????????????????????????????????????????

    private static bool EvaluateCondition(ImportCondition condition, ImportedTransaction txn)
    {
        return condition switch
        {
            TextCondition tc => EvalText(tc, txn),
            AmountCondition ac => EvalAmount(ac, txn),
            CurrencyCondition cc => string.Equals(cc.Code, txn.CurrencyCode, StringComparison.OrdinalIgnoreCase),
            DateCondition dc => EvalDate(dc, txn),
            AndCondition and => and.Children.All(c => EvaluateCondition(c, txn)),
            OrCondition or => or.Children.Any(c => EvaluateCondition(c, txn)),
            NotCondition not => not.Child != null && !EvaluateCondition(not.Child, txn),
            _ => false
        };
    }

    private static bool EvalText(TextCondition tc, ImportedTransaction txn)
    {
        var fieldValue = tc.Field switch
        {
            "Description" => txn.Description,
            "Reference" => txn.Reference,
            "Counterparty" => txn.Counterparty,
            _ => txn.Description
        };

        var comparison = tc.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return tc.Mode switch
        {
            StringMatchMode.Contains => fieldValue.Contains(tc.Value, comparison),
            StringMatchMode.Equals => string.Equals(fieldValue, tc.Value, comparison),
            StringMatchMode.StartsWith => fieldValue.StartsWith(tc.Value, comparison),
            StringMatchMode.EndsWith => fieldValue.EndsWith(tc.Value, comparison),
            StringMatchMode.Regex => SafeRegexMatch(fieldValue, tc.Value, tc.IgnoreCase),
            _ => false
        };
    }

    private static bool SafeRegexMatch(string input, string pattern, bool ignoreCase)
    {
        if (string.IsNullOrEmpty(pattern) || pattern.Length > 200)
            return false;
        try
        {
            var options = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            return Regex.IsMatch(input, pattern, options, TimeSpan.FromMilliseconds(100));
        }
        catch { return false; }
    }

    private static bool EvalAmount(AmountCondition ac, ImportedTransaction txn)
    {
        var amt = Math.Abs(txn.Amount);
        var tol = ac.ToleranceAbs;
        if (ac.TolerancePct > 0 && ac.Value1 != 0)
            tol = Math.Max(tol, Math.Abs(ac.Value1) * ac.TolerancePct / 100m);

        return ac.Mode switch
        {
            NumberMatchMode.Equals => Math.Abs(amt - ac.Value1) <= tol,
            NumberMatchMode.Between => amt >= ac.Value1 - tol && amt <= ac.Value2 + tol,
            NumberMatchMode.GreaterThan => amt > ac.Value1 - tol,
            NumberMatchMode.LessThan => amt < ac.Value1 + tol,
            _ => false
        };
    }

    private static bool EvalDate(DateCondition dc, ImportedTransaction txn)
    {
        return dc.Mode switch
        {
            DateMatchMode.Equals => dc.Date1.HasValue && txn.TxnDate == dc.Date1.Value,
            DateMatchMode.Between => dc.Date1.HasValue && dc.Date2.HasValue &&
                                     txn.TxnDate >= dc.Date1.Value && txn.TxnDate <= dc.Date2.Value,
            DateMatchMode.Weekday => dc.Date1.HasValue &&
                                     txn.TxnDate.DayOfWeek == dc.Date1.Value.DayOfWeek,
            DateMatchMode.Month => dc.Date1.HasValue &&
                                   txn.TxnDate.Month == dc.Date1.Value.Month,
            _ => false
        };
    }

    // ????????????????????????????????????????????
    // Suggestion building
    // ????????????????????????????????????????????

    private static ImportSuggestion? BuildSuggestion(
        ImportedTransaction txn, ImportRuleRecord rule, ImportRuleAction action, BillingState? billingState)
    {
        var explain = new ImportRuleExplain(
            rule.RuleId, rule.Version, rule.PackId, rule.Kind, rule.Priority,
            $"Rule '{rule.Kind}' (pack={rule.PackId:N}, pri={rule.Priority}) matched.");

        return action switch
        {
            CategorizeAction cat => new ImportSuggestion
            {
                ImportedTransactionId = txn.ImportedId,
                Kind = txn.Direction == "credit" ? SuggestionKind.ApplyIncome : SuggestionKind.ApplyExpense,
                Confidence = 70 + Math.Min(rule.Priority, 25),
                ProposedCategory = cat.CategoryName,
                Notes = cat.NotesTemplate ?? txn.Description,
                Explain = new List<ImportRuleExplain> { explain },
                DeterministicSuggestionId = ImportSuggestion.ComputeSuggestionId(
                    txn.ImportedId, rule.RuleId, rule.Version, $"Categorize:{cat.CategoryName}")
            },
            IgnoreAction ign => new ImportSuggestion
            {
                ImportedTransactionId = txn.ImportedId,
                Kind = SuggestionKind.Ignore,
                Confidence = 80 + Math.Min(rule.Priority, 15),
                Notes = ign.Reason,
                Explain = new List<ImportRuleExplain> { explain },
                DeterministicSuggestionId = ImportSuggestion.ComputeSuggestionId(
                    txn.ImportedId, rule.RuleId, rule.Version, $"Ignore:{ign.Reason}")
            },
            MatchBillAction mb => TryMatchBill(txn, rule, mb, billingState, explain),
            MatchInvoiceAction mi => TryMatchInvoice(txn, rule, mi, billingState, explain),
            MatchTransferAction mt => new ImportSuggestion
            {
                ImportedTransactionId = txn.ImportedId,
                Kind = SuggestionKind.ApplyTransfer,
                Confidence = 60 + Math.Min(rule.Priority, 20),
                Notes = txn.Description,
                Explain = new List<ImportRuleExplain> { explain },
                DeterministicSuggestionId = ImportSuggestion.ComputeSuggestionId(
                    txn.ImportedId, rule.RuleId, rule.Version, "Transfer")
            },
            RouteAccountAction ra => new ImportSuggestion
            {
                ImportedTransactionId = txn.ImportedId,
                Kind = txn.Direction == "credit" ? SuggestionKind.ApplyIncome : SuggestionKind.ApplyExpense,
                Confidence = 50 + Math.Min(rule.Priority, 20),
                ProposedAccountId = ra.AccountId,
                Notes = txn.Description,
                Explain = new List<ImportRuleExplain> { explain },
                DeterministicSuggestionId = ImportSuggestion.ComputeSuggestionId(
                    txn.ImportedId, rule.RuleId, rule.Version, $"Route:{ra.AccountId}")
            },
            _ => null
        };
    }

    private static ImportSuggestion? TryMatchBill(
        ImportedTransaction txn, ImportRuleRecord rule, MatchBillAction mb,
        BillingState? billingState, ImportRuleExplain explain)
    {
        if (billingState == null) return null;

        var amt = Math.Abs(txn.Amount);
        var tol = mb.Tolerance;

        var matchedBill = billingState.Bills.Values
            .Where(b => !b.IsCancelled && !b.IsWrittenOff && b.Outstanding > 0)
            .Where(b => Math.Abs(b.Outstanding - amt) <= tol)
            .OrderBy(b => Math.Abs(b.Outstanding - amt))
            .ThenBy(b => b.BillId)
            .FirstOrDefault();

        if (matchedBill == null) return null;

        var conf = Math.Abs(matchedBill.Outstanding - amt) <= 0.01m ? 95 : 80;

        return new ImportSuggestion
        {
            ImportedTransactionId = txn.ImportedId,
            Kind = SuggestionKind.PayBill,
            Confidence = conf,
            ProposedRelatedEntityId = matchedBill.BillId,
            Notes = $"Bill {matchedBill.Reference} outstanding={matchedBill.Outstanding:F2}",
            Explain = new List<ImportRuleExplain>
            {
                explain,
                new(rule.RuleId, rule.Version, rule.PackId, "MatchBill", rule.Priority,
                    $"Matched bill {matchedBill.BillId} (outstanding={matchedBill.Outstanding:F2}, txnAmount={amt:F2})")
            },
            DeterministicSuggestionId = ImportSuggestion.ComputeSuggestionId(
                txn.ImportedId, rule.RuleId, rule.Version, $"PayBill:{matchedBill.BillId}")
        };
    }

    private static ImportSuggestion? TryMatchInvoice(
        ImportedTransaction txn, ImportRuleRecord rule, MatchInvoiceAction mi,
        BillingState? billingState, ImportRuleExplain explain)
    {
        if (billingState == null) return null;

        var amt = Math.Abs(txn.Amount);
        var tol = mi.Tolerance;

        var matchedInvoice = billingState.Invoices.Values
            .Where(i => !i.IsCancelled && !i.IsWrittenOff && i.Outstanding > 0)
            .Where(i => Math.Abs(i.Outstanding - amt) <= tol)
            .OrderBy(i => Math.Abs(i.Outstanding - amt))
            .ThenBy(i => i.InvoiceId)
            .FirstOrDefault();

        if (matchedInvoice == null) return null;

        var conf = Math.Abs(matchedInvoice.Outstanding - amt) <= 0.01m ? 95 : 80;

        return new ImportSuggestion
        {
            ImportedTransactionId = txn.ImportedId,
            Kind = SuggestionKind.ReceiveInvoice,
            Confidence = conf,
            ProposedRelatedEntityId = matchedInvoice.InvoiceId,
            Notes = $"Invoice {matchedInvoice.Reference} outstanding={matchedInvoice.Outstanding:F2}",
            Explain = new List<ImportRuleExplain>
            {
                explain,
                new(rule.RuleId, rule.Version, rule.PackId, "MatchInvoice", rule.Priority,
                    $"Matched invoice {matchedInvoice.InvoiceId} (outstanding={matchedInvoice.Outstanding:F2}, txnAmount={amt:F2})")
            },
            DeterministicSuggestionId = ImportSuggestion.ComputeSuggestionId(
                txn.ImportedId, rule.RuleId, rule.Version, $"ReceiveInvoice:{matchedInvoice.InvoiceId}")
        };
    }
}
