using DebtManager.Domain.ImportRules;

namespace DebtManager.Domain.Projections;

public sealed class ImportRulesState
{
    public Dictionary<Guid, ImportRulePackRecord> Packs { get; set; } = new();
    public Dictionary<Guid, List<ImportRuleRecord>> RulesByPack { get; set; } = new();

    public List<ImportRuleRecord> GetActiveRulesFlattened()
    {
        var result = new List<ImportRuleRecord>();

        foreach (var pack in Packs.Values)
        {
            if (!pack.IsEnabled || pack.IsArchived)
                continue;

            if (!RulesByPack.TryGetValue(pack.PackId, out var rules))
                continue;

            // Group by RuleId, take highest version that is not archived
            var latestByRuleId = rules
                .GroupBy(r => r.RuleId)
                .Select(g => g.Where(r => !r.IsArchived).OrderByDescending(r => r.Version).FirstOrDefault())
                .Where(r => r != null && r.IsEnabled)
                .Cast<ImportRuleRecord>();

            result.AddRange(latestByRuleId);
        }

        result.Sort((a, b) =>
        {
            int cmp = b.Priority.CompareTo(a.Priority);
            if (cmp != 0) return cmp;
            return a.RuleId.CompareTo(b.RuleId);
        });

        return result;
    }
}
