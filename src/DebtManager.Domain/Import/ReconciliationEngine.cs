using DebtManager.Domain.Projections;

namespace DebtManager.Domain.Import;

/// <summary>
/// Proposed match between an imported bank transaction and an existing ledger event.
/// </summary>
public sealed record ReconciliationCandidate(
    Guid ImportedId,
    DateOnly TxnDate,
    decimal Amount,
    string Description,
    string Direction,
    string Status, // "Unmatched", "Matched", "Applied", "Ignored"
    Guid? MatchedEventId,
    string? MatchType, // "Exact", "Near", null
    decimal Confidence // 0.0 to 1.0
);

/// <summary>
/// Deterministic reconciliation engine.
/// Given imported rows and existing ledger rows, produces proposed matches.
/// </summary>
public static class ReconciliationEngine
{
    public static IReadOnlyList<ReconciliationCandidate> Reconcile(
        BankImportState importState,
        CashLedgerState ledgerState,
        Guid accountId)
    {
        var results = new List<ReconciliationCandidate>();

        foreach (var imported in importState.ImportedTransactions.Values
            .Where(t => t.AccountId == accountId))
        {
            // Check if already matched/applied/ignored
            if (importState.MatchedLinks.ContainsKey(imported.ImportedId))
            {
                var match = importState.MatchedLinks[imported.ImportedId];
                results.Add(new ReconciliationCandidate(
                    imported.ImportedId, imported.TxnDate, imported.Amount,
                    imported.Description, imported.Direction,
                    "Matched", match.MatchedEventId, match.MatchType, match.Confidence));
                continue;
            }

            if (importState.AppliedLinks.ContainsKey(imported.ImportedId))
            {
                var applied = importState.AppliedLinks[imported.ImportedId];
                results.Add(new ReconciliationCandidate(
                    imported.ImportedId, imported.TxnDate, imported.Amount,
                    imported.Description, imported.Direction,
                    "Applied", applied.AppliedEventId, applied.AppliedType, 1.0m));
                continue;
            }

            if (importState.IgnoredIds.Contains(imported.ImportedId))
            {
                results.Add(new ReconciliationCandidate(
                    imported.ImportedId, imported.TxnDate, imported.Amount,
                    imported.Description, imported.Direction,
                    "Ignored", null, null, 0m));
                continue;
            }

            // Find best match from ledger
            var (bestMatchId, bestMatchType, bestConfidence) = FindBestMatch(imported, ledgerState, accountId);

            results.Add(new ReconciliationCandidate(
                imported.ImportedId, imported.TxnDate, imported.Amount,
                imported.Description, imported.Direction,
                bestMatchId.HasValue ? "SuggestedMatch" : "Unmatched",
                bestMatchId, bestMatchType, bestConfidence));
        }

        return results.OrderBy(r => r.TxnDate).ThenBy(r => r.ImportedId).ToList();
    }

    private static (Guid? EventId, string? MatchType, decimal Confidence) FindBestMatch(
        ImportedTransaction imported,
        CashLedgerState ledgerState,
        Guid accountId)
    {
        Guid? bestId = null;
        string? bestType = null;
        decimal bestConf = 0m;

        var expectedDir = imported.Direction == "debit" ? "Out" : "In";

        foreach (var row in ledgerState.Rows.Where(r => r.AccountId == accountId))
        {
            if (row.Direction != expectedDir && row.Direction != "Transfer") continue;

            // Exact: same date + same amount
            if (row.EffectiveDate == imported.TxnDate && row.Amount == imported.Amount)
            {
                var descScore = DescriptionSimilarity(imported.Description, row.Reference, row.Category, row.Notes);
                var confidence = 0.8m + (descScore * 0.2m);
                if (confidence > bestConf)
                {
                    bestId = row.EventId;
                    bestType = "Exact";
                    bestConf = confidence;
                }
            }
            // Near: ±2 days + same amount
            else if (Math.Abs(row.EffectiveDate.DayNumber - imported.TxnDate.DayNumber) <= 2 &&
                     row.Amount == imported.Amount)
            {
                var descScore = DescriptionSimilarity(imported.Description, row.Reference, row.Category, row.Notes);
                var dayPenalty = Math.Abs(row.EffectiveDate.DayNumber - imported.TxnDate.DayNumber) * 0.05m;
                var confidence = 0.6m + (descScore * 0.2m) - dayPenalty;
                if (confidence > bestConf)
                {
                    bestId = row.EventId;
                    bestType = "Near";
                    bestConf = Math.Max(0.1m, confidence);
                }
            }
        }

        return (bestId, bestType, bestConf);
    }

    private static decimal DescriptionSimilarity(string importedDesc, string reference, string category, string notes)
    {
        if (string.IsNullOrWhiteSpace(importedDesc)) return 0m;

        var tokens = importedDesc.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length > 2)
            .ToList();

        if (tokens.Count == 0) return 0m;

        var combined = $"{reference} {category} {notes}".ToLowerInvariant();
        var matched = tokens.Count(t => combined.Contains(t));

        return (decimal)matched / tokens.Count;
    }
}
