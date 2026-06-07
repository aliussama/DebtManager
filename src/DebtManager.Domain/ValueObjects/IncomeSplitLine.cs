namespace DebtManager.Domain.ValueObjects;

/// <summary>
/// A single line in a split income transaction.
/// </summary>
public sealed record IncomeSplitLine(string Source, Money Amount, string? Notes = null);
