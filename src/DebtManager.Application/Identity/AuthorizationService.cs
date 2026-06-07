using DebtManager.Domain.Events;
using DebtManager.Domain.Identity;
using DebtManager.Domain.Projections;

namespace DebtManager.Application.Identity;

public sealed class AuthorizationService
{
    private readonly IEventStore _store;

    public AuthorizationService(IEventStore store) => _store = store;

    public async Task<IdentityContext> BuildContextAsync(Guid userId, Guid? sessionId = null, CancellationToken ct = default)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = IdentityProjector.Project(all);
        return BuildContext(state, userId, sessionId);
    }

    public static IdentityContext BuildContext(IdentityState state, Guid userId, Guid? sessionId = null)
    {
        var perms = state.GetEffectivePermissions(userId);
        return new IdentityContext
        {
            CurrentUserId = userId,
            CurrentSessionId = sessionId,
            EffectivePermissions = perms
        };
    }

    public static void CheckPermission(IdentityContext ctx, string permissionCode)
    {
        ctx.Require(permissionCode);
    }
}
