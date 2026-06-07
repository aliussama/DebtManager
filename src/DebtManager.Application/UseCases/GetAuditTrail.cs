using DebtManager.Domain.Events;
using DebtManager.Domain.Rules;

namespace DebtManager.Application.UseCases;

/// <summary>
/// DTO for an audit trail row.
/// </summary>
public sealed record AuditTrailRowDto(
    DateTimeOffset At,
    DateOnly EffectiveDate,
    string Category,
    string Severity,
    string Message,
    Guid? RelatedEventId,
    Guid? ObligationId,
    string? ObligationName
);

/// <summary>
/// Query for retrieving audit trail entries.
/// </summary>
public sealed record GetAuditTrailQuery(
    Guid? ObligationId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null
);

/// <summary>
/// Handler for retrieving audit trail entries from financial snapshots.
/// For v1, this iterates all obligations and collects audit entries from their snapshots.
/// </summary>
public sealed class GetAuditTrailHandler
{
    private readonly GetFinancialSnapshotHandler _snapshotHandler;
    private readonly GetObligationsListHandler _obligationsHandler;

    public GetAuditTrailHandler(
        GetFinancialSnapshotHandler snapshotHandler,
        GetObligationsListHandler obligationsHandler)
    {
        _snapshotHandler = snapshotHandler;
        _obligationsHandler = obligationsHandler;
    }

    public async Task<IReadOnlyList<AuditTrailRowDto>> HandleAsync(
        GetAuditTrailQuery query,
        CancellationToken ct = default)
    {
        var results = new List<AuditTrailRowDto>();

        // Determine the as-of date for snapshots (use ToDate or today)
        var asOfDate = query.ToDate ?? DateOnly.FromDateTime(DateTime.Today);

        // Get obligation name map
        var obligations = await _obligationsHandler.HandleAsync(asOfDate, "EGP", ct);
        var obligationNameMap = obligations.ToDictionary(o => o.ObligationId, o => o.Name);

        // Determine which obligations to query
        IEnumerable<Guid> obligationIds;
        if (query.ObligationId.HasValue)
        {
            obligationIds = new[] { query.ObligationId.Value };
        }
        else
        {
            obligationIds = obligations.Select(o => o.ObligationId);
        }

        // Collect audit entries from each obligation's snapshot
        foreach (var obligationId in obligationIds)
        {
            try
            {
                var snapshot = await _snapshotHandler.HandleAsync(obligationId, asOfDate, ct);

                var obligationName = obligationNameMap.TryGetValue(obligationId, out var name) ? name : null;

                foreach (var entry in snapshot.Audit)
                {
                    // Apply server-side date filtering if specified
                    if (query.FromDate.HasValue && entry.EffectiveDate < query.FromDate.Value)
                        continue;
                    if (query.ToDate.HasValue && entry.EffectiveDate > query.ToDate.Value)
                        continue;

                    results.Add(new AuditTrailRowDto(
                        At: entry.At,
                        EffectiveDate: entry.EffectiveDate,
                        Category: entry.Category,
                        Severity: entry.Severity ?? "Info",
                        Message: entry.Message,
                        RelatedEventId: entry.RelatedEventId,
                        ObligationId: entry.ObligationId ?? obligationId,
                        ObligationName: obligationName
                    ));
                }
            }
            catch
            {
                // Skip obligations that fail to load (e.g., no schedule defined)
                // In production, we might want to log this
            }
        }

        // Sort by EffectiveDate desc, then At desc
        return results
            .OrderByDescending(r => r.EffectiveDate)
            .ThenByDescending(r => r.At)
            .ToList();
    }
}
