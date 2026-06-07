using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ImportRules;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

public static class ImportRulesProjector
{
    public static ImportRulesState Project(IEnumerable<EventEnvelope> envelopes)
    {
        var state = new ImportRulesState();
        var opt = DomainJson.Options;

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value);

        foreach (var env in ordered)
        {
            switch (env.EventType)
            {
                case nameof(ImportRulePackCreated):
                {
                    var ev = JsonSerializer.Deserialize<ImportRulePackCreated>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Packs[ev.PackId] = new ImportRulePackRecord
                    {
                        PackId = ev.PackId,
                        Name = ev.Name,
                        Description = ev.Description,
                        IsEnabled = ev.IsEnabled,
                        IsArchived = false
                    };
                    break;
                }
                case nameof(ImportRulePackModified):
                {
                    var ev = JsonSerializer.Deserialize<ImportRulePackModified>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Packs.TryGetValue(ev.PackId, out var pack))
                    {
                        pack.Name = ev.Name;
                        pack.Description = ev.Description;
                        pack.IsEnabled = ev.IsEnabled;
                    }
                    break;
                }
                case nameof(ImportRulePackArchived):
                {
                    var ev = JsonSerializer.Deserialize<ImportRulePackArchived>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Packs.TryGetValue(ev.PackId, out var pack))
                        pack.IsArchived = true;
                    break;
                }
                case nameof(ImportRuleDefined):
                {
                    var ev = JsonSerializer.Deserialize<ImportRuleDefined>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (!state.RulesByPack.ContainsKey(ev.PackId))
                        state.RulesByPack[ev.PackId] = new List<ImportRuleRecord>();
                    state.RulesByPack[ev.PackId].Add(new ImportRuleRecord
                    {
                        PackId = ev.PackId,
                        RuleId = ev.RuleId,
                        Version = ev.Version,
                        Kind = ev.RuleKind,
                        Priority = ev.Priority,
                        IsEnabled = ev.IsEnabled,
                        IsArchived = false,
                        MatchSpecJson = ev.MatchSpecJson,
                        ActionSpecJson = ev.ActionSpecJson,
                        CreatedDate = ev.EffectiveDate
                    });
                    break;
                }
                case nameof(ImportRuleArchived):
                {
                    var ev = JsonSerializer.Deserialize<ImportRuleArchived>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.RulesByPack.TryGetValue(ev.PackId, out var rules))
                    {
                        foreach (var r in rules.Where(r => r.RuleId == ev.RuleId))
                            r.IsArchived = true;
                    }
                    break;
                }
            }
        }

        return state;
    }
}
