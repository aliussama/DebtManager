using DebtManager.Domain.Identity;

namespace DebtManager.Domain.Projections;

public sealed class IdentityState
{
    public Dictionary<Guid, VaultUserRecord> Users { get; } = new();
    public Dictionary<Guid, HashSet<string>> PermissionOverridesGranted { get; } = new();
    public Dictionary<Guid, HashSet<string>> PermissionOverridesRevoked { get; } = new();
    public Dictionary<Guid, UserSessionRecord> Sessions { get; } = new();

    public VaultUserRecord? GetUser(Guid userId)
        => Users.TryGetValue(userId, out var u) ? u : null;

    public IReadOnlyList<VaultUserRecord> GetActiveUsers()
        => Users.Values.Where(u => u.IsActive && !u.IsArchived).ToList();

    public HashSet<string> GetEffectivePermissions(Guid userId)
    {
        if (!Users.TryGetValue(userId, out var user))
            return new HashSet<string>();

        if (!user.IsActive || user.IsArchived)
            return new HashSet<string>();

        PermissionOverridesGranted.TryGetValue(userId, out var granted);
        PermissionOverridesRevoked.TryGetValue(userId, out var revoked);

        return PermissionMatrix.Resolve(user.Role, granted, revoked);
    }

    public bool IsSessionActive(Guid sessionId)
        => Sessions.TryGetValue(sessionId, out var s) && s.IsActive;

    public IdentitySummary GetSummary()
    {
        var activeUsers = Users.Values.Count(u => u.IsActive && !u.IsArchived);
        var activeSessions = Sessions.Values.Count(s => s.IsActive);
        var hasOwner = Users.Values.Any(u => u.Role == VaultUserRole.Owner && u.IsActive && !u.IsArchived);
        return new IdentitySummary(activeUsers, activeSessions, hasOwner);
    }
}
