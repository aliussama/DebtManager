namespace DebtManager.Domain.Quality;

/// <summary>
/// Immutable financial health score (0-100) with grade (A-F).
/// All components are deterministically computed from projection states.
/// </summary>
public sealed record HealthScore(
    int Score,
    string Grade,
    IReadOnlyList<HealthComponent> Components
);

/// <summary>
/// A single component of the health score with its weight and status.
/// </summary>
public sealed record HealthComponent(
    string Name,
    decimal Value,
    decimal Weight,
    string Status
);
