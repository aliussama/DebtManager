using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Identity;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

public static class IdentityProjector
{
    public static IdentityState Project(IEnumerable<EventEnvelope> envelopes)
    {
        var state = new IdentityState();
        var opt = DomainJson.Options;

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value);

        foreach (var env in ordered)
        {
            switch (env.EventType)
            {
                case nameof(VaultUserCreated):
                {
                    var ev = JsonSerializer.Deserialize<VaultUserCreated>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Users[ev.UserId] = new VaultUserRecord
                    {
                        UserId = ev.UserId,
                        Username = ev.Username,
                        DisplayName = ev.DisplayName,
                        Role = Enum.TryParse<VaultUserRole>(ev.RoleCode, true, out var r) ? r : VaultUserRole.Viewer,
                        IsActive = ev.IsActive,
                        CreatedDate = ev.EffectiveDate,
                        LastModifiedDate = ev.EffectiveDate
                    };
                    break;
                }
                case nameof(VaultUserModified):
                {
                    var ev = JsonSerializer.Deserialize<VaultUserModified>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Users.TryGetValue(ev.UserId, out var user))
                    {
                        user.DisplayName = ev.DisplayName;
                        user.Role = Enum.TryParse<VaultUserRole>(ev.RoleCode, true, out var r) ? r : user.Role;
                        user.IsActive = ev.IsActive;
                        user.LastModifiedDate = ev.EffectiveDate;
                    }
                    break;
                }
                case nameof(VaultUserArchived):
                {
                    var ev = JsonSerializer.Deserialize<VaultUserArchived>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Users.TryGetValue(ev.UserId, out var user))
                    {
                        user.IsArchived = true;
                        user.IsActive = false;
                        user.ArchiveReason = ev.Reason;
                        user.LastModifiedDate = ev.EffectiveDate;
                    }
                    break;
                }
                case nameof(VaultUserSecretSet):
                {
                    var ev = JsonSerializer.Deserialize<VaultUserSecretSet>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Users.TryGetValue(ev.UserId, out var user))
                    {
                        user.PasswordHashBase64 = ev.PasswordHashBase64;
                        user.PasswordSaltBase64 = ev.PasswordSaltBase64;
                        user.PasswordAlgoVersion = ev.PasswordAlgoVersion;
                    }
                    break;
                }
                case nameof(VaultUserSecretRotated):
                {
                    var ev = JsonSerializer.Deserialize<VaultUserSecretRotated>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Users.TryGetValue(ev.UserId, out var user))
                    {
                        user.PasswordHashBase64 = ev.NewPasswordHashBase64;
                        user.PasswordSaltBase64 = ev.NewPasswordSaltBase64;
                        user.PasswordAlgoVersion = ev.PasswordAlgoVersion;
                    }
                    break;
                }
                case nameof(UserSessionStarted):
                {
                    var ev = JsonSerializer.Deserialize<UserSessionStarted>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Sessions[ev.SessionId] = new UserSessionRecord
                    {
                        SessionId = ev.SessionId,
                        UserId = ev.UserId,
                        DeviceId = ev.DeviceId,
                        ClientVersion = ev.ClientVersion,
                        StartedDate = ev.EffectiveDate
                    };
                    break;
                }
                case nameof(UserSessionEnded):
                {
                    var ev = JsonSerializer.Deserialize<UserSessionEnded>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Sessions.TryGetValue(ev.SessionId, out var session))
                    {
                        session.EndedDate = ev.EffectiveDate;
                        session.EndReason = ev.Reason;
                    }
                    break;
                }
                case nameof(PermissionOverrideGranted):
                {
                    var ev = JsonSerializer.Deserialize<PermissionOverrideGranted>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (!state.PermissionOverridesGranted.ContainsKey(ev.UserId))
                        state.PermissionOverridesGranted[ev.UserId] = new HashSet<string>();
                    state.PermissionOverridesGranted[ev.UserId].Add(ev.PermissionCode);
                    // Remove from revoked if present
                    if (state.PermissionOverridesRevoked.TryGetValue(ev.UserId, out var rev))
                        rev.Remove(ev.PermissionCode);
                    break;
                }
                case nameof(PermissionOverrideRevoked):
                {
                    var ev = JsonSerializer.Deserialize<PermissionOverrideRevoked>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (!state.PermissionOverridesRevoked.ContainsKey(ev.UserId))
                        state.PermissionOverridesRevoked[ev.UserId] = new HashSet<string>();
                    state.PermissionOverridesRevoked[ev.UserId].Add(ev.PermissionCode);
                    // Remove from granted if present
                    if (state.PermissionOverridesGranted.TryGetValue(ev.UserId, out var gr))
                        gr.Remove(ev.PermissionCode);
                    break;
                }
            }
        }

        return state;
    }
}
