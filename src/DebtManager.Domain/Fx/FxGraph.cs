using DebtManager.Domain.Projections;

namespace DebtManager.Domain.Fx;

/// <summary>
/// Builds a directed graph of currency nodes with dated rate series.
/// Supports direct, inverse, and multi-hop FX conversion with deterministic path selection.
/// </summary>
public sealed class FxGraph
{
    // Adjacency: from -> to -> list of (date, rate)
    private readonly Dictionary<string, Dictionary<string, List<(DateOnly Date, decimal Rate)>>> _edges = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _nodes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Build the graph from AssetsState FxRatePoints.
    /// Each recorded rate adds both directions (direct + inverse).
    /// </summary>
    public static FxGraph Build(IReadOnlyList<FxRatePoint> fxRates)
    {
        var graph = new FxGraph();

        foreach (var rate in fxRates)
        {
            if (rate.Rate == 0m) continue;

            graph._nodes.Add(rate.FromCurrencyCode);
            graph._nodes.Add(rate.ToCurrencyCode);

            graph.AddEdge(rate.FromCurrencyCode, rate.ToCurrencyCode, rate.AsOfDate, rate.Rate);
            graph.AddEdge(rate.ToCurrencyCode, rate.FromCurrencyCode, rate.AsOfDate, 1m / rate.Rate);
        }

        return graph;
    }

    private void AddEdge(string from, string to, DateOnly date, decimal rate)
    {
        if (!_edges.TryGetValue(from, out var neighbours))
        {
            neighbours = new Dictionary<string, List<(DateOnly, decimal)>>(StringComparer.OrdinalIgnoreCase);
            _edges[from] = neighbours;
        }

        if (!neighbours.TryGetValue(to, out var series))
        {
            series = new List<(DateOnly, decimal)>();
            neighbours[to] = series;
        }

        series.Add((date, rate));
    }

    /// <summary>
    /// Try to get an FX rate from one currency to another on a given date using the specified policy.
    /// Returns true if a valid rate was found.
    /// </summary>
    public bool TryGetRate(string from, string to, DateOnly date, FxPolicyConfig config,
        out decimal rate, out FxConversionResult meta)
    {
        // Same currency
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            rate = 1m;
            meta = FxConversionResult.Identity(from.ToUpperInvariant());
            return true;
        }

        // BFS for shortest-hop path(s), then pick lexicographic smallest
        var path = FindBestPath(from, to, date, config);

        if (path == null)
        {
            rate = 0m;
            meta = FxConversionResult.Unknown($"No FX path from {from} to {to} as-of {date}");
            return false;
        }

        // Compute compound rate along path
        decimal compoundRate = 1m;
        DateOnly latestRateDate = DateOnly.MinValue;
        var pathParts = new List<string> { path[0].ToUpperInvariant() };

        for (int i = 0; i < path.Count - 1; i++)
        {
            var edgeResult = SelectRate(_edges[path[i]][path[i + 1]], date, config);
            if (edgeResult == null)
            {
                rate = 0m;
                meta = FxConversionResult.Unknown($"No rate for {path[i]}->{path[i + 1]} within policy constraints");
                return false;
            }

            compoundRate *= edgeResult.Value.Rate;
            if (edgeResult.Value.Date > latestRateDate)
                latestRateDate = edgeResult.Value.Date;

            pathParts.Add(path[i + 1].ToUpperInvariant());
        }

        rate = compoundRate;
        meta = new FxConversionResult(
            true,
            compoundRate,
            string.Join(">", pathParts),
            string.Empty,
            latestRateDate,
            path.Count - 1);
        return true;
    }

    /// <summary>
    /// BFS to find all shortest-hop paths, then pick the lexicographically smallest path string.
    /// Deterministic: same inputs always produce same path.
    /// </summary>
    private List<string>? FindBestPath(string from, string to, DateOnly date, FxPolicyConfig config)
    {
        // BFS
        var queue = new Queue<List<string>>();
        queue.Enqueue(new List<string> { from });

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { from };
        var candidatePaths = new List<List<string>>();
        int? shortestHops = null;

        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            var current = path[^1];

            // If we already found paths at a shorter hop count, stop deeper exploration
            if (shortestHops.HasValue && path.Count > shortestHops.Value)
                break;

            if (!_edges.TryGetValue(current, out var neighbours))
                continue;

            foreach (var (neighbour, series) in neighbours)
            {
                // Check if there's a valid rate along this edge
                var edgeRate = SelectRate(series, date, config);
                if (edgeRate == null) continue;

                var newPath = new List<string>(path) { neighbour };

                if (string.Equals(neighbour, to, StringComparison.OrdinalIgnoreCase))
                {
                    shortestHops = newPath.Count;
                    candidatePaths.Add(newPath);
                    continue;
                }

                if (!visited.Contains(neighbour))
                {
                    visited.Add(neighbour);
                    queue.Enqueue(newPath);
                }
            }
        }

        if (candidatePaths.Count == 0)
            return null;

        // Deterministic tie-breaker: lexicographically smallest path string
        return candidatePaths
            .OrderBy(p => string.Join(">", p.Select(c => c.ToUpperInvariant())), StringComparer.Ordinal)
            .First();
    }

    /// <summary>
    /// Select a rate from the series based on the valuation policy and max age constraint.
    /// </summary>
    private static (DateOnly Date, decimal Rate)? SelectRate(
        List<(DateOnly Date, decimal Rate)> series, DateOnly date, FxPolicyConfig config)
    {
        var maxAge = config.MaxAgeDays;
        var minDate = date.AddDays(-maxAge);
        var maxDate = date.AddDays(maxAge);

        switch (config.Policy)
        {
            case FxValuationPolicy.Spot:
            {
                var exact = series.Where(r => r.Date == date).OrderByDescending(r => r.Rate).FirstOrDefault();
                return exact.Rate != 0 && exact.Date == date ? exact : null;
            }

            case FxValuationPolicy.NearestBefore:
            case FxValuationPolicy.EodNearestBefore:
            {
                var candidates = series
                    .Where(r => r.Date <= date && r.Date >= minDate)
                    .OrderByDescending(r => r.Date)
                    .ThenByDescending(r => r.Rate);
                var best = candidates.FirstOrDefault();
                return best.Rate != 0 || best.Date != default ? best : null;
            }

            case FxValuationPolicy.NearestAfter:
            {
                var candidates = series
                    .Where(r => r.Date >= date && r.Date <= maxDate)
                    .OrderBy(r => r.Date)
                    .ThenByDescending(r => r.Rate);
                var best = candidates.FirstOrDefault();
                return best.Rate != 0 || best.Date != default ? best : null;
            }

            case FxValuationPolicy.Nearest:
            {
                var candidates = series
                    .Where(r => r.Date >= minDate && r.Date <= maxDate)
                    .Select(r => (r.Date, r.Rate, Diff: Math.Abs(r.Date.DayNumber - date.DayNumber)))
                    .OrderBy(r => r.Diff)
                    .ThenBy(r => r.Date <= date ? 0 : 1) // Tie-breaker: prefer before
                    .ThenByDescending(r => r.Rate);
                var best = candidates.FirstOrDefault();
                return best.Rate != 0 || best.Date != default ? (best.Date, best.Rate) : null;
            }

            case FxValuationPolicy.InterpolateLinear:
            {
                var before = series
                    .Where(r => r.Date <= date && r.Date >= minDate)
                    .OrderByDescending(r => r.Date)
                    .FirstOrDefault();

                var after = series
                    .Where(r => r.Date >= date && r.Date <= maxDate)
                    .OrderBy(r => r.Date)
                    .FirstOrDefault();

                if (before.Rate != 0 && before.Date != default && after.Rate != 0 && after.Date != default)
                {
                    if (before.Date == after.Date)
                        return before;

                    var totalDays = (decimal)(after.Date.DayNumber - before.Date.DayNumber);
                    var daysFromBefore = (decimal)(date.DayNumber - before.Date.DayNumber);
                    var interpolated = before.Rate + (after.Rate - before.Rate) * (daysFromBefore / totalDays);
                    return (date, interpolated);
                }

                // Fallback to NearestBefore
                return before.Rate != 0 && before.Date != default ? before : null;
            }

            default:
                return null;
        }
    }
}
