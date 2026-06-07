using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DebtManager.Application.Identity;
using DebtManager.Domain.Events;
using DebtManager.Domain.Identity;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

internal static class IdentityStreams
{
    public static readonly StreamId IdentityStream = new(Guid.Parse("1DE00000-0040-0001-0001-000000000001"));
}

// --- Commands ---
public sealed record CreateVaultUserCommand(string Username, string DisplayName, string RoleCode, DateOnly EffectiveDate);
public sealed record ModifyVaultUserCommand(Guid UserId, string DisplayName, string RoleCode, bool IsActive, DateOnly EffectiveDate);
public sealed record ArchiveVaultUserCommand(Guid UserId, string Reason, DateOnly EffectiveDate);
public sealed record SetUserPasswordCommand(Guid UserId, string Password, DateOnly EffectiveDate);
public sealed record LoginCommand(string Username, string Password, Guid DeviceId, string ClientVersion, DateOnly EffectiveDate);
public sealed record LogoutCommand(Guid SessionId, DateOnly EffectiveDate);
public sealed record GrantPermissionOverrideCommand(Guid UserId, string PermissionCode, DateOnly EffectiveDate);
public sealed record RevokePermissionOverrideCommand(Guid UserId, string PermissionCode, DateOnly EffectiveDate);

// --- DTOs ---
public sealed record VaultUserDto(Guid UserId, string Username, string DisplayName, string RoleCode, bool IsActive, bool IsArchived, DateOnly CreatedDate);
public sealed record LoginResultDto(bool Success, Guid? SessionId, Guid? UserId, string? ErrorMessage, IReadOnlyList<string>? Permissions);
public sealed record WhoAmIDto(Guid UserId, string Username, string DisplayName, string RoleCode, IReadOnlyList<string> Permissions);

// --- Password Hashing ---
internal static class PasswordHasher
{
    public const int CurrentAlgoVersion = 1;
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static (string HashBase64, string SaltBase64) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string password, string hashBase64, string saltBase64)
    {
        var salt = Convert.FromBase64String(saltBase64);
        var expectedHash = Convert.FromBase64String(hashBase64);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}

// --- Handlers ---

public sealed class CreateVaultUserHandler
{
    private readonly IEventStore _store;
    public CreateVaultUserHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(CreateVaultUserCommand cmd, Guid actorUserId, Guid deviceId, IdentityContext? ctx = null, CancellationToken ct = default)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = IdentityProjector.Project(all);

        // First user must be Owner, no auth required
        bool isFirstUser = state.Users.Count == 0;
        if (!isFirstUser)
        {
            if (ctx != null) ctx.Require(VaultPermission.PERM_MANAGE_USERS);
        }

        // Uniqueness check
        if (state.Users.Values.Any(u => u.Username.Equals(cmd.Username, StringComparison.OrdinalIgnoreCase) && !u.IsArchived))
            throw new InvalidOperationException($"Username '{cmd.Username}' already exists.");

        var userId = Guid.NewGuid();
        var roleCode = isFirstUser ? nameof(VaultUserRole.Owner) : cmd.RoleCode;
        var ev = new VaultUserCreated(userId, cmd.Username, cmd.DisplayName, roleCode, true, cmd.EffectiveDate);
        await AppendAsync(ev, actorUserId, deviceId, ct);
        return userId;
    }

    private async Task AppendAsync<T>(T domainEvent, Guid actorUserId, Guid deviceId, CancellationToken ct) where T : IDomainEvent
    {
        var typeName = typeof(T).Name;
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), IdentityStreams.IdentityStream,
            typeName, DateTimeOffset.UtcNow, domainEvent.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(domainEvent, DomainJson.Options)), ct);
    }
}

public sealed class ModifyVaultUserHandler
{
    private readonly IEventStore _store;
    public ModifyVaultUserHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ModifyVaultUserCommand cmd, Guid actorUserId, Guid deviceId, IdentityContext? ctx = null, CancellationToken ct = default)
    {
        if (ctx != null) ctx.Require(VaultPermission.PERM_MANAGE_USERS);

        var ev = new VaultUserModified(cmd.UserId, cmd.DisplayName, cmd.RoleCode, cmd.IsActive, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), IdentityStreams.IdentityStream,
            nameof(VaultUserModified), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class ArchiveVaultUserHandler
{
    private readonly IEventStore _store;
    public ArchiveVaultUserHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveVaultUserCommand cmd, Guid actorUserId, Guid deviceId, IdentityContext? ctx = null, CancellationToken ct = default)
    {
        if (ctx != null) ctx.Require(VaultPermission.PERM_MANAGE_USERS);

        // Verify the user is Owner (only Owner can archive)
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = IdentityProjector.Project(all);
        if (ctx != null)
        {
            var actor = state.GetUser(ctx.CurrentUserId!.Value);
            if (actor?.Role != VaultUserRole.Owner)
                throw new UnauthorizedAccessException("Only Owner can archive users.");
        }

        var ev = new VaultUserArchived(cmd.UserId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), IdentityStreams.IdentityStream,
            nameof(VaultUserArchived), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class SetUserPasswordHandler
{
    private readonly IEventStore _store;
    public SetUserPasswordHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(SetUserPasswordCommand cmd, Guid actorUserId, Guid deviceId, IdentityContext? ctx = null, CancellationToken ct = default)
    {
        // Allow self-set or admin/owner
        if (ctx != null && ctx.CurrentUserId != cmd.UserId)
            ctx.Require(VaultPermission.PERM_MANAGE_USERS);

        var (hash, salt) = PasswordHasher.Hash(cmd.Password);
        var ev = new VaultUserSecretSet(cmd.UserId, hash, salt, PasswordHasher.CurrentAlgoVersion, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), IdentityStreams.IdentityStream,
            nameof(VaultUserSecretSet), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class LoginHandler
{
    private readonly IEventStore _store;
    public LoginHandler(IEventStore store) => _store = store;

    public async Task<LoginResultDto> HandleAsync(LoginCommand cmd, Guid actorUserId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = IdentityProjector.Project(all);

        var user = state.Users.Values.FirstOrDefault(u =>
            u.Username.Equals(cmd.Username, StringComparison.OrdinalIgnoreCase));

        if (user == null)
            return new LoginResultDto(false, null, null, "Invalid username or password.", null);

        if (user.IsArchived || !user.IsActive)
            return new LoginResultDto(false, null, null, "Account is disabled or archived.", null);

        if (string.IsNullOrEmpty(user.PasswordHashBase64) || string.IsNullOrEmpty(user.PasswordSaltBase64))
            return new LoginResultDto(false, null, null, "Password not set for this account.", null);

        if (!PasswordHasher.Verify(cmd.Password, user.PasswordHashBase64, user.PasswordSaltBase64))
            return new LoginResultDto(false, null, null, "Invalid username or password.", null);

        var sessionId = Guid.NewGuid();
        var ev = new UserSessionStarted(sessionId, user.UserId, cmd.DeviceId, cmd.ClientVersion, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), IdentityStreams.IdentityStream,
            nameof(UserSessionStarted), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            user.UserId, cmd.DeviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);

        var perms = state.GetEffectivePermissions(user.UserId);
        return new LoginResultDto(true, sessionId, user.UserId, null, perms.ToList());
    }
}

public sealed class LogoutHandler
{
    private readonly IEventStore _store;
    public LogoutHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(LogoutCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = IdentityProjector.Project(all);

        if (!state.Sessions.TryGetValue(cmd.SessionId, out var session))
            return; // idempotent
        if (!session.IsActive)
            return;

        var ev = new UserSessionEnded(cmd.SessionId, session.UserId, "UserLogout", cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), IdentityStreams.IdentityStream,
            nameof(UserSessionEnded), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class WhoAmIHandler
{
    private readonly IEventStore _store;
    public WhoAmIHandler(IEventStore store) => _store = store;

    public async Task<WhoAmIDto?> HandleAsync(Guid userId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = IdentityProjector.Project(all);
        var user = state.GetUser(userId);
        if (user == null) return null;
        var perms = state.GetEffectivePermissions(userId);
        return new WhoAmIDto(user.UserId, user.Username, user.DisplayName, user.Role.ToString(), perms.ToList());
    }
}

public sealed class ListVaultUsersHandler
{
    private readonly IEventStore _store;
    public ListVaultUsersHandler(IEventStore store) => _store = store;

    public async Task<IReadOnlyList<VaultUserDto>> HandleAsync(CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = IdentityProjector.Project(all);
        return state.Users.Values
            .OrderBy(u => u.Username)
            .Select(u => new VaultUserDto(u.UserId, u.Username, u.DisplayName, u.Role.ToString(), u.IsActive, u.IsArchived, u.CreatedDate))
            .ToList();
    }
}

public sealed class GrantPermissionOverrideHandler
{
    private readonly IEventStore _store;
    public GrantPermissionOverrideHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(GrantPermissionOverrideCommand cmd, Guid actorUserId, Guid deviceId, IdentityContext? ctx = null, CancellationToken ct = default)
    {
        if (ctx != null) ctx.Require(VaultPermission.PERM_MANAGE_USERS);

        // Only Owner can grant overrides
        if (ctx != null)
        {
            var all2 = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
            var s = IdentityProjector.Project(all2);
            var actor = s.GetUser(ctx.CurrentUserId!.Value);
            if (actor?.Role != VaultUserRole.Owner)
                throw new UnauthorizedAccessException("Only Owner can grant permission overrides.");
        }

        var ev = new PermissionOverrideGranted(cmd.UserId, cmd.PermissionCode, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), IdentityStreams.IdentityStream,
            nameof(PermissionOverrideGranted), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class RevokePermissionOverrideHandler
{
    private readonly IEventStore _store;
    public RevokePermissionOverrideHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(RevokePermissionOverrideCommand cmd, Guid actorUserId, Guid deviceId, IdentityContext? ctx = null, CancellationToken ct = default)
    {
        if (ctx != null) ctx.Require(VaultPermission.PERM_MANAGE_USERS);

        if (ctx != null)
        {
            var all2 = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
            var s = IdentityProjector.Project(all2);
            var actor = s.GetUser(ctx.CurrentUserId!.Value);
            if (actor?.Role != VaultUserRole.Owner)
                throw new UnauthorizedAccessException("Only Owner can revoke permission overrides.");
        }

        var ev = new PermissionOverrideRevoked(cmd.UserId, cmd.PermissionCode, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), IdentityStreams.IdentityStream,
            nameof(PermissionOverrideRevoked), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}
