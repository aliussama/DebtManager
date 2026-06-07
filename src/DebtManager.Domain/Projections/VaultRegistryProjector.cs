using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Vault;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

/// <summary>
/// Deterministic projector for vault registry events.
/// Ordering: EffectiveDate -> OccurredAt -> EventId.
/// </summary>
public static class VaultRegistryProjector
{
    public static VaultRegistryState Project(IEnumerable<EventEnvelope> envelopes)
    {
        var state = new VaultRegistryState();
        var opt = DomainJson.Options;

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value);

        foreach (var env in ordered)
        {
            switch (env.EventType)
            {
                case nameof(VaultCreated):
                {
                    var ev = JsonSerializer.Deserialize<VaultCreated>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Vaults[ev.VaultId] = new VaultDescriptor
                    {
                        VaultId = ev.VaultId,
                        Name = ev.Name,
                        CurrencyCode = ev.CurrencyCode,
                        CreatedDate = ev.CreatedDate
                    };
                    break;
                }
                case nameof(VaultRenamed):
                {
                    var ev = JsonSerializer.Deserialize<VaultRenamed>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Vaults.TryGetValue(ev.VaultId, out var v))
                        state.Vaults[ev.VaultId] = v with { Name = ev.NewName };
                    break;
                }
                case nameof(VaultArchived):
                {
                    var ev = JsonSerializer.Deserialize<VaultArchived>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Vaults.TryGetValue(ev.VaultId, out var v))
                        state.Vaults[ev.VaultId] = v with { IsArchived = true };
                    break;
                }
                case nameof(ActiveVaultSelected):
                {
                    var ev = JsonSerializer.Deserialize<ActiveVaultSelected>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.ActiveVaultId = ev.VaultId;
                    if (state.Vaults.TryGetValue(ev.VaultId, out var v))
                        state.Vaults[ev.VaultId] = v with { LastOpenedAtUtc = env.OccurredAt };
                    break;
                }
            }
        }

        return state;
    }
}
