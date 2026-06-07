namespace DebtManager.Domain.Identity;

public static class PermissionMatrix
{
    private static readonly IReadOnlyDictionary<VaultUserRole, HashSet<string>> _rolePermissions =
        new Dictionary<VaultUserRole, HashSet<string>>
        {
            [VaultUserRole.Owner] = new(new[]
            {
                VaultPermission.PERM_READ_ALL,
                VaultPermission.PERM_WRITE_CASH,
                VaultPermission.PERM_WRITE_BILLING,
                VaultPermission.PERM_WRITE_IMPORT,
                VaultPermission.PERM_WRITE_ASSETS,
                VaultPermission.PERM_WRITE_INVESTMENTS,
                VaultPermission.PERM_WRITE_TAX,
                VaultPermission.PERM_WRITE_PLANNING,
                VaultPermission.PERM_MANAGE_USERS,
                VaultPermission.PERM_MANAGE_SETTINGS,
                VaultPermission.PERM_EXPORT_DATA,
                VaultPermission.PERM_PURGE_BLOBS,
                VaultPermission.PERM_APPROVE_AI_PROPOSALS,
                VaultPermission.PERM_RUN_AI_ANALYSIS,
                VaultPermission.PERM_RUN_DATA_QUALITY,
                VaultPermission.PERM_APPLY_DATA_FIXES
            }),
            [VaultUserRole.Admin] = new(new[]
            {
                VaultPermission.PERM_READ_ALL,
                VaultPermission.PERM_WRITE_CASH,
                VaultPermission.PERM_WRITE_BILLING,
                VaultPermission.PERM_WRITE_IMPORT,
                VaultPermission.PERM_WRITE_ASSETS,
                VaultPermission.PERM_WRITE_INVESTMENTS,
                VaultPermission.PERM_WRITE_TAX,
                VaultPermission.PERM_WRITE_PLANNING,
                VaultPermission.PERM_MANAGE_USERS,
                VaultPermission.PERM_MANAGE_SETTINGS,
                VaultPermission.PERM_EXPORT_DATA,
                VaultPermission.PERM_APPROVE_AI_PROPOSALS,
                VaultPermission.PERM_RUN_AI_ANALYSIS,
                VaultPermission.PERM_RUN_DATA_QUALITY,
                VaultPermission.PERM_APPLY_DATA_FIXES
            }),
            [VaultUserRole.Accountant] = new(new[]
            {
                VaultPermission.PERM_READ_ALL,
                VaultPermission.PERM_WRITE_CASH,
                VaultPermission.PERM_WRITE_BILLING,
                VaultPermission.PERM_WRITE_IMPORT,
                VaultPermission.PERM_WRITE_ASSETS,
                VaultPermission.PERM_WRITE_INVESTMENTS,
                VaultPermission.PERM_WRITE_TAX,
                VaultPermission.PERM_WRITE_PLANNING,
                VaultPermission.PERM_EXPORT_DATA,
                VaultPermission.PERM_RUN_DATA_QUALITY
            }),
            [VaultUserRole.Auditor] = new(new[]
            {
                VaultPermission.PERM_READ_ALL,
                VaultPermission.PERM_EXPORT_DATA,
                VaultPermission.PERM_RUN_DATA_QUALITY
            }),
            [VaultUserRole.Viewer] = new(new[]
            {
                VaultPermission.PERM_READ_ALL
            })
        };

    public static HashSet<string> GetBasePermissions(VaultUserRole role)
    {
        return _rolePermissions.TryGetValue(role, out var perms)
            ? new HashSet<string>(perms)
            : new HashSet<string>();
    }

    public static HashSet<string> Resolve(
        VaultUserRole role,
        IEnumerable<string>? grantedOverrides,
        IEnumerable<string>? revokedOverrides)
    {
        var effective = GetBasePermissions(role);
        if (grantedOverrides != null)
            foreach (var g in grantedOverrides) effective.Add(g);
        if (revokedOverrides != null)
            foreach (var r in revokedOverrides) effective.Remove(r);
        return effective;
    }
}
