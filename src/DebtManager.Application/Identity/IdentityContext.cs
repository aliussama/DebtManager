using DebtManager.Domain.Identity;

namespace DebtManager.Application.Identity;

public sealed class IdentityContext
{
    public Guid? CurrentUserId { get; set; }
    public Guid? CurrentSessionId { get; set; }
    public bool IsAuthenticated => CurrentUserId.HasValue;
    public HashSet<string> EffectivePermissions { get; set; } = new();

    public void Require(string permissionCode)
    {
        if (!IsAuthenticated)
            throw new UnauthorizedAccessException("Not authenticated.");
        if (!EffectivePermissions.Contains(permissionCode))
            throw new UnauthorizedAccessException($"Permission denied: {permissionCode}");
    }

    public bool HasPermission(string permissionCode)
        => IsAuthenticated && EffectivePermissions.Contains(permissionCode);

    /// <summary>
    /// Creates a context with all permissions (for backward compatibility / system use).
    /// </summary>
    public static IdentityContext System() => new()
    {
        CurrentUserId = Guid.Empty,
        EffectivePermissions = PermissionMatrix.GetBasePermissions(VaultUserRole.Owner)
    };
}
