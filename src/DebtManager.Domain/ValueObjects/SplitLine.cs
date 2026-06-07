namespace DebtManager.Domain.ValueObjects;

/// <summary>
/// A single line in a split expense transaction.
/// </summary>
public sealed record SplitLine(string Category, Money Amount, string? Notes);
