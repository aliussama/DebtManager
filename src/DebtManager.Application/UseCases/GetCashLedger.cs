using DebtManager.Application.Projections;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;

namespace DebtManager.Application.UseCases;

public sealed record CashLedgerQuery(
    Guid? AccountId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    bool IncludeTransfers = true
);

public sealed record CashLedgerRowDto(
    Guid EventId,
    DateOnly EffectiveDate,
    Guid AccountId,
    string AccountName,
    string Direction,
    decimal Amount,
    string CurrencyCode,
    string Category,
    string Reference,
    string Notes,
    string RelatedAccountName,
    Guid CorrelationId,
    Guid? SourceId = null
);

public sealed record CashLedgerResultDto(
    IReadOnlyList<CashLedgerRowDto> Rows,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal NetCashflow
);

public sealed class GetCashLedgerHandler
{
    private readonly IEventStore _store;
    private readonly ProjectionRunner? _runner;

    public GetCashLedgerHandler(IEventStore store, ProjectionRunner? runner = null)
    {
        _store = store;
        _runner = runner;
    }

    public async Task<CashLedgerResultDto> HandleAsync(CashLedgerQuery query, CancellationToken ct)
    {
        CashLedgerState state;
        if (_runner != null)
        {
            state = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
                envelopes => CashLedgerProjector.Project(envelopes, query.ToDate),
                asOfDate: query.ToDate,
                ct: ct);
        }
        else
        {
            var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
            state = CashLedgerProjector.Project(envelopes, query.ToDate);
        }

        var rows = state.Rows.AsEnumerable();

        if (query.AccountId.HasValue)
        {
            var id = query.AccountId.Value;
            rows = rows.Where(r => r.AccountId == id || r.RelatedAccountId == id);
        }

        if (query.FromDate.HasValue)
            rows = rows.Where(r => r.EffectiveDate >= query.FromDate.Value);

        if (!query.IncludeTransfers)
            rows = rows.Where(r => r.Direction != "Transfer");

        var filteredRows = rows.OrderByDescending(r => r.EffectiveDate)
            .ThenByDescending(r => r.OccurredAt)
            .ToList();

        // Enrich income rows with SourceId from IncomeSourceProjector (Option A: query-time enrichment)
        IEnumerable<EventEnvelope> sourceEnvelopes;
        if (_runner != null)
        {
            // Re-use the same envelopes if available; fallback to re-read
            sourceEnvelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        }
        else
        {
            sourceEnvelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        }
        var sourceState = IncomeSourceProjector.Project(sourceEnvelopes);

        var dtos = filteredRows.Select(r =>
        {
            Guid? sourceId = r.SourceId;
            // If no SourceId set and this is an income row, try to match by Reference (Source string)
            if (!sourceId.HasValue && r.Direction == "In" && r.Category == "Income" && !string.IsNullOrEmpty(r.Reference))
            {
                var matched = sourceState.FindByName(r.Reference);
                if (matched != null)
                    sourceId = matched.SourceId;
            }
            return new CashLedgerRowDto(
                r.EventId,
                r.EffectiveDate,
                r.AccountId,
                r.AccountName,
                r.Direction,
                r.Amount,
                r.CurrencyCode,
                r.Category,
                r.Reference,
                r.Notes,
                r.RelatedAccountName,
                r.CorrelationId,
                sourceId
            );
        }).ToList();

        // Compute totals from filtered set
        var totalIn = filteredRows.Where(r => r.Direction == "In").Sum(r => r.Amount);
        var totalOut = filteredRows.Where(r => r.Direction == "Out").Sum(r => r.Amount);

        return new CashLedgerResultDto(dtos, totalIn, totalOut, totalIn - totalOut);
    }
}
