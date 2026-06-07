using DebtManager.Application.Identity;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Identity;
using DebtManager.Domain.Projections;
using DebtManager.Infrastructure.Identity;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class IdentityAndPermissionsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly string _vaultPath;

    public IdentityAndPermissionsTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"IdentityTests_{id}.db");
        _factory = new SqliteConnectionFactory(_dbPath, new TestKeyStore());
        _eventStore = new SqliteEventStore(_factory);
        _vaultPath = Path.Combine(Path.GetTempPath(), $"identity_vault_{id}.bin");
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        for (int i = 0; i < 30; i++)
        {
            try
            {
                if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
                if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
                if (File.Exists(_dbPath)) File.Delete(_dbPath);
                if (File.Exists(_vaultPath)) File.Delete(_vaultPath);
                break;
            }
            catch (IOException) when (i < 29) { Thread.Sleep(100); }
        }
    }

    private async Task<Guid> CreateOwnerAsync(string username = "admin", string password = "P@ssw0rd!")
    {
        var createHandler = new CreateVaultUserHandler(_eventStore);
        var userId = await createHandler.HandleAsync(
            new CreateVaultUserCommand(username, "Admin User", "Owner", DateOnly.FromDateTime(DateTime.Today)),
            Guid.Empty, _deviceId);

        var pwHandler = new SetUserPasswordHandler(_eventStore);
        await pwHandler.HandleAsync(
            new SetUserPasswordCommand(userId, password, DateOnly.FromDateTime(DateTime.Today)),
            userId, _deviceId);

        return userId;
    }

    private async Task<LoginResultDto> LoginAsync(string username, string password)
    {
        var handler = new LoginHandler(_eventStore);
        return await handler.HandleAsync(
            new LoginCommand(username, password, _deviceId, "1.0.0", DateOnly.FromDateTime(DateTime.Today)),
            Guid.Empty, CancellationToken.None);
    }

    [Fact]
    public async Task CreateFirstOwnerUser_Works_AndHasAllPermissions()
    {
        var userId = await CreateOwnerAsync();

        var whoAmI = await new WhoAmIHandler(_eventStore).HandleAsync(userId, CancellationToken.None);
        Assert.NotNull(whoAmI);
        Assert.Equal("Owner", whoAmI!.RoleCode);
        Assert.Contains(VaultPermission.PERM_MANAGE_USERS, whoAmI.Permissions);
        Assert.Contains(VaultPermission.PERM_WRITE_CASH, whoAmI.Permissions);
        Assert.Contains(VaultPermission.PERM_APPROVE_AI_PROPOSALS, whoAmI.Permissions);
        Assert.Contains(VaultPermission.PERM_PURGE_BLOBS, whoAmI.Permissions);
        Assert.Contains(VaultPermission.PERM_APPLY_DATA_FIXES, whoAmI.Permissions);
    }

    [Fact]
    public async Task Login_StartsSession_AndWhoAmIReturnsPermissions()
    {
        await CreateOwnerAsync("admin", "P@ssw0rd!");

        var result = await LoginAsync("admin", "P@ssw0rd!");
        Assert.True(result.Success);
        Assert.NotNull(result.SessionId);
        Assert.NotNull(result.UserId);
        Assert.NotNull(result.Permissions);
        Assert.True(result.Permissions!.Count > 0);

        var whoAmI = await new WhoAmIHandler(_eventStore).HandleAsync(result.UserId!.Value, CancellationToken.None);
        Assert.NotNull(whoAmI);
        Assert.True(whoAmI!.Permissions.Count > 0);
    }

    [Fact]
    public async Task RememberMe_SavesToken_AndAutoRestoresSession()
    {
        var userId = await CreateOwnerAsync("admin", "P@ssw0rd!");
        var loginResult = await LoginAsync("admin", "P@ssw0rd!");
        Assert.True(loginResult.Success);

        var vault = new LocalAuthVault(new TestKeyStore(), _vaultPath);
        vault.SaveRememberedSession(loginResult.SessionId!.Value, loginResult.UserId!.Value);

        var restored = vault.LoadRememberedSession();
        Assert.NotNull(restored);
        Assert.Equal(loginResult.SessionId!.Value, restored!.Value.SessionId);
        Assert.Equal(loginResult.UserId!.Value, restored.Value.UserId);

        // Verify session is still active in event store
        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state = IdentityProjector.Project(all);
        Assert.True(state.IsSessionActive(restored.Value.SessionId));
    }

    [Fact]
    public async Task Logout_EndsSession_AndClearsRememberedToken()
    {
        var userId = await CreateOwnerAsync("admin", "P@ssw0rd!");
        var loginResult = await LoginAsync("admin", "P@ssw0rd!");
        Assert.True(loginResult.Success);

        var vault = new LocalAuthVault(new TestKeyStore(), _vaultPath);
        vault.SaveRememberedSession(loginResult.SessionId!.Value, loginResult.UserId!.Value);

        // Logout
        var logoutHandler = new LogoutHandler(_eventStore);
        await logoutHandler.HandleAsync(
            new LogoutCommand(loginResult.SessionId!.Value, DateOnly.FromDateTime(DateTime.Today)),
            userId, _deviceId, CancellationToken.None);

        vault.ClearRememberedSession();

        // Session ended
        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state = IdentityProjector.Project(all);
        Assert.False(state.IsSessionActive(loginResult.SessionId!.Value));

        // Remembered session cleared
        Assert.Null(vault.LoadRememberedSession());
    }

    [Fact]
    public void RoleMatrix_AdminHasExpectedPermissions()
    {
        var adminPerms = PermissionMatrix.GetBasePermissions(VaultUserRole.Admin);

        Assert.Contains(VaultPermission.PERM_READ_ALL, adminPerms);
        Assert.Contains(VaultPermission.PERM_WRITE_CASH, adminPerms);
        Assert.Contains(VaultPermission.PERM_MANAGE_USERS, adminPerms);
        Assert.Contains(VaultPermission.PERM_MANAGE_SETTINGS, adminPerms);
        Assert.Contains(VaultPermission.PERM_APPROVE_AI_PROPOSALS, adminPerms);
        // Admin does NOT have PERM_PURGE_BLOBS
        Assert.DoesNotContain(VaultPermission.PERM_PURGE_BLOBS, adminPerms);
    }

    [Fact]
    public async Task PermissionOverride_GrantAndRevoke_ChangesEffectivePermissions()
    {
        var ownerId = await CreateOwnerAsync("owner", "P@ssw0rd!");
        var ownerCtx = new IdentityContext
        {
            CurrentUserId = ownerId,
            EffectivePermissions = PermissionMatrix.GetBasePermissions(VaultUserRole.Owner)
        };

        // Create Viewer user
        var createHandler = new CreateVaultUserHandler(_eventStore);
        var viewerId = await createHandler.HandleAsync(
            new CreateVaultUserCommand("viewer1", "Viewer User", "Viewer", DateOnly.FromDateTime(DateTime.Today)),
            ownerId, _deviceId, ownerCtx);

        // Viewer shouldn't have PERM_WRITE_CASH
        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state = IdentityProjector.Project(all);
        var viewerPerms = state.GetEffectivePermissions(viewerId);
        Assert.DoesNotContain(VaultPermission.PERM_WRITE_CASH, viewerPerms);

        // Grant override
        var grantHandler = new GrantPermissionOverrideHandler(_eventStore);
        await grantHandler.HandleAsync(
            new GrantPermissionOverrideCommand(viewerId, VaultPermission.PERM_WRITE_CASH, DateOnly.FromDateTime(DateTime.Today)),
            ownerId, _deviceId, ownerCtx);

        all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        state = IdentityProjector.Project(all);
        viewerPerms = state.GetEffectivePermissions(viewerId);
        Assert.Contains(VaultPermission.PERM_WRITE_CASH, viewerPerms);

        // Revoke override
        var revokeHandler = new RevokePermissionOverrideHandler(_eventStore);
        await revokeHandler.HandleAsync(
            new RevokePermissionOverrideCommand(viewerId, VaultPermission.PERM_WRITE_CASH, DateOnly.FromDateTime(DateTime.Today)),
            ownerId, _deviceId, ownerCtx);

        all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        state = IdentityProjector.Project(all);
        viewerPerms = state.GetEffectivePermissions(viewerId);
        Assert.DoesNotContain(VaultPermission.PERM_WRITE_CASH, viewerPerms);
    }

    [Fact]
    public async Task ArchivedUser_CannotLogin()
    {
        var ownerId = await CreateOwnerAsync("owner", "P@ssw0rd!");
        var ownerCtx = new IdentityContext
        {
            CurrentUserId = ownerId,
            EffectivePermissions = PermissionMatrix.GetBasePermissions(VaultUserRole.Owner)
        };

        // Create second user
        var createHandler = new CreateVaultUserHandler(_eventStore);
        var userId = await createHandler.HandleAsync(
            new CreateVaultUserCommand("testuser", "Test User", "Accountant", DateOnly.FromDateTime(DateTime.Today)),
            ownerId, _deviceId, ownerCtx);

        var pwHandler = new SetUserPasswordHandler(_eventStore);
        await pwHandler.HandleAsync(
            new SetUserPasswordCommand(userId, "UserPass1!", DateOnly.FromDateTime(DateTime.Today)),
            ownerId, _deviceId, ownerCtx);

        // Login works
        var result1 = await LoginAsync("testuser", "UserPass1!");
        Assert.True(result1.Success);

        // Archive user
        var archiveHandler = new ArchiveVaultUserHandler(_eventStore);
        await archiveHandler.HandleAsync(
            new ArchiveVaultUserCommand(userId, "No longer needed", DateOnly.FromDateTime(DateTime.Today)),
            ownerId, _deviceId, ownerCtx);

        // Login fails
        var result2 = await LoginAsync("testuser", "UserPass1!");
        Assert.False(result2.Success);
        Assert.Contains("disabled or archived", result2.ErrorMessage!);
    }

    [Fact]
    public async Task Authorization_BlockWriteWithoutPermission()
    {
        var ownerId = await CreateOwnerAsync("owner", "P@ssw0rd!");
        var ownerCtx = new IdentityContext
        {
            CurrentUserId = ownerId,
            EffectivePermissions = PermissionMatrix.GetBasePermissions(VaultUserRole.Owner)
        };

        // Create Viewer user
        var createHandler = new CreateVaultUserHandler(_eventStore);
        var viewerId = await createHandler.HandleAsync(
            new CreateVaultUserCommand("viewer2", "Viewer Two", "Viewer", DateOnly.FromDateTime(DateTime.Today)),
            ownerId, _deviceId, ownerCtx);

        // Build Viewer context
        var viewerCtx = new IdentityContext
        {
            CurrentUserId = viewerId,
            EffectivePermissions = PermissionMatrix.GetBasePermissions(VaultUserRole.Viewer)
        };

        // Attempt RecordExpense without PERM_WRITE_CASH
        var expenseHandler = new RecordExpenseHandler(_eventStore);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            expenseHandler.HandleAsync(
                new RecordExpenseCommand(100m, "EGP", DateOnly.FromDateTime(DateTime.Today), "Food", "Test"),
                viewerId, _deviceId, CancellationToken.None, viewerCtx));
    }

    [Fact]
    public async Task Authorization_AllowsWriteWithPermission()
    {
        var ownerId = await CreateOwnerAsync("owner", "P@ssw0rd!");

        // Build Owner context
        var ownerCtx = new IdentityContext
        {
            CurrentUserId = ownerId,
            EffectivePermissions = PermissionMatrix.GetBasePermissions(VaultUserRole.Owner)
        };

        // Create account first (needed for expense)
        var accountHandler = new CreateAccountHandler(_eventStore);
        await accountHandler.HandleAsync(
            new CreateAccountCommand(null, "Cash", "Bank", 5000m, "EGP", DateOnly.FromDateTime(DateTime.Today)),
            ownerId, _deviceId, CancellationToken.None);

        // RecordExpense with PERM_WRITE_CASH should succeed
        var expenseHandler = new RecordExpenseHandler(_eventStore);
        await expenseHandler.HandleAsync(
            new RecordExpenseCommand(50m, "EGP", DateOnly.FromDateTime(DateTime.Today), "Food", "Lunch"),
            ownerId, _deviceId, CancellationToken.None, ownerCtx);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        Assert.Contains(all, e => e.EventType == nameof(ExpenseRecorded));
    }

    [Fact]
    public async Task DeterministicProjection_SameEventsSameState()
    {
        var ownerId = await CreateOwnerAsync("owner", "P@ssw0rd!");
        var ownerCtx = new IdentityContext
        {
            CurrentUserId = ownerId,
            EffectivePermissions = PermissionMatrix.GetBasePermissions(VaultUserRole.Owner)
        };

        var createHandler = new CreateVaultUserHandler(_eventStore);
        var user2 = await createHandler.HandleAsync(
            new CreateVaultUserCommand("user2", "User Two", "Accountant", DateOnly.FromDateTime(DateTime.Today)),
            ownerId, _deviceId, ownerCtx);

        var grantHandler = new GrantPermissionOverrideHandler(_eventStore);
        await grantHandler.HandleAsync(
            new GrantPermissionOverrideCommand(user2, VaultPermission.PERM_PURGE_BLOBS, DateOnly.FromDateTime(DateTime.Today)),
            ownerId, _deviceId, ownerCtx);

        // Project twice from same events
        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state1 = IdentityProjector.Project(all);
        var state2 = IdentityProjector.Project(all);

        Assert.Equal(state1.Users.Count, state2.Users.Count);
        foreach (var (userId, user1) in state1.Users)
        {
            Assert.True(state2.Users.ContainsKey(userId));
            var u2 = state2.Users[userId];
            Assert.Equal(user1.Username, u2.Username);
            Assert.Equal(user1.Role, u2.Role);
            Assert.Equal(user1.IsActive, u2.IsActive);
            Assert.Equal(user1.IsArchived, u2.IsArchived);
        }

        var perms1 = state1.GetEffectivePermissions(user2);
        var perms2 = state2.GetEffectivePermissions(user2);
        Assert.True(perms1.SetEquals(perms2));
        Assert.Contains(VaultPermission.PERM_PURGE_BLOBS, perms1);

        var summary1 = state1.GetSummary();
        var summary2 = state2.GetSummary();
        Assert.Equal(summary1.ActiveUserCount, summary2.ActiveUserCount);
        Assert.Equal(summary1.HasOwner, summary2.HasOwner);
    }
}
