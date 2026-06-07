using System.Text.RegularExpressions;

namespace DebtManager.Domain.Vault;

/// <summary>
/// Describes a single vault in the global registry.
/// This is a GLOBAL concept, not a financial entity.
/// </summary>
public sealed record VaultDescriptor
{
    public Guid VaultId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string CurrencyCode { get; init; } = "EGP";
    public DateOnly CreatedDate { get; init; }
    public bool IsArchived { get; init; }
    public DateTimeOffset? LastOpenedAtUtc { get; init; }
}

/// <summary>
/// Full manifest of all vaults + the currently active one.
/// </summary>
public sealed record VaultManifest
{
    public IReadOnlyList<VaultDescriptor> Vaults { get; init; } = [];
    public Guid? ActiveVaultId { get; init; }
}

/// <summary>
/// Resolved file paths for a single vault.
/// </summary>
public sealed record VaultPaths(
    string DbPath,
    string KeyPath,
    string AuthVaultPath,
    string BlobsPath,
    string ExportsPath
);

/// <summary>
/// Pure validation helpers for vault metadata.
/// </summary>
public static class VaultValidation
{
    private static readonly Regex CurrencyRegex = new(@"^[A-Z]{3}$", RegexOptions.Compiled);

    public const int MaxNameLength = 100;
    public const int MinNameLength = 1;
    public const int MaxVaults = 10;

    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Vault name is required.");
        if (name.Trim().Length < MinNameLength)
            throw new InvalidOperationException($"Vault name must be at least {MinNameLength} character(s).");
        if (name.Trim().Length > MaxNameLength)
            throw new InvalidOperationException($"Vault name must not exceed {MaxNameLength} characters.");
    }

    public static void ValidateCurrencyCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || !CurrencyRegex.IsMatch(code))
            throw new InvalidOperationException("Currency code must be exactly 3 uppercase letters (e.g. USD, EGP).");
    }
}
