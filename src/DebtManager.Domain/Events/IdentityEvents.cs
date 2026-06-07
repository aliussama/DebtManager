namespace DebtManager.Domain.Events;

public sealed record VaultUserCreated(
    Guid UserId,
    string Username,
    string DisplayName,
    string RoleCode,
    bool IsActive,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record VaultUserModified(
    Guid UserId,
    string DisplayName,
    string RoleCode,
    bool IsActive,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record VaultUserArchived(
    Guid UserId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record VaultUserSecretSet(
    Guid UserId,
    string PasswordHashBase64,
    string PasswordSaltBase64,
    int PasswordAlgoVersion,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record VaultUserSecretRotated(
    Guid UserId,
    string NewPasswordHashBase64,
    string NewPasswordSaltBase64,
    int PasswordAlgoVersion,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record UserSessionStarted(
    Guid SessionId,
    Guid UserId,
    Guid DeviceId,
    string ClientVersion,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record UserSessionEnded(
    Guid SessionId,
    Guid UserId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record PermissionOverrideGranted(
    Guid UserId,
    string PermissionCode,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record PermissionOverrideRevoked(
    Guid UserId,
    string PermissionCode,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
