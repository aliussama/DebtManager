using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Commands ---

public sealed record DefineIncomeSourceCommand(
    string Name,
    IncomeSourceType SourceType,
    string CurrencyCode,
    bool IsRecurring,
    DateOnly EffectiveDate,
    string? Notes
);

public sealed record ArchiveIncomeSourceCommand(
    Guid SourceId,
    string Reason,
    DateOnly EffectiveDate
);

// --- DTOs ---

public sealed record IncomeSourceDto(
    Guid SourceId,
    string Name,
    string SourceType,
    string CurrencyCode,
    bool IsRecurring,
    bool IsArchived,
    decimal TotalReceived,
    DateOnly? LastReceivedDate,
    DateOnly CreatedEffectiveDate,
    string? Notes
);

public sealed record IncomeBySourceRow(
    Guid? SourceId,
    string SourceName,
    string SourceType,
    decimal Total,
    string CurrencyCode,
    int TransactionCount
);

public sealed record IncomeBySourceReportDto(
    IReadOnlyList<IncomeBySourceRow> PerSourceTotals,
    IncomeBySourceRow Unclassified,
    decimal GrandTotal
);

// --- Handlers ---

public sealed class DefineIncomeSourceHandler
{
    private readonly IEventStore _store;

    public DefineIncomeSourceHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(DefineIncomeSourceCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var name = cmd.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name) || name.Length > 100)
            throw new InvalidOperationException("Income source name must be 1..100 characters.");

        var currency = cmd.CurrencyCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (currency.Length != 3 || !currency.All(char.IsLetter))
            throw new InvalidOperationException("CurrencyCode must be exactly 3 uppercase letters.");

        if (!Enum.IsDefined(cmd.SourceType))
            throw new InvalidOperationException($"Invalid SourceType: {cmd.SourceType}");

        if (cmd.Notes != null && cmd.Notes.Length > 500)
            throw new InvalidOperationException("Notes must be at most 500 characters.");

        var sourceId = Guid.NewGuid();

        var ev = new IncomeSourceDefined(
            sourceId,
            name,
            cmd.SourceType,
            currency,
            cmd.IsRecurring,
            cmd.EffectiveDate,
            cmd.Notes
        );

        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(sourceId),
            nameof(IncomeSourceDefined),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            actorUserId,
            deviceId,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options)
        );

        await _store.AppendAsync(env, ct);
        return sourceId;
    }
}

public sealed class ArchiveIncomeSourceHandler
{
    private readonly IEventStore _store;

    public ArchiveIncomeSourceHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveIncomeSourceCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = IncomeSourceProjector.Project(envelopes);

        var source = state.TryGet(cmd.SourceId);
        if (source == null)
            throw new InvalidOperationException($"Income source {cmd.SourceId} not found.");

        if (source.IsArchived)
            return; // idempotent

        var ev = new IncomeSourceArchived(cmd.SourceId, cmd.Reason, cmd.EffectiveDate);

        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(cmd.SourceId),
            nameof(IncomeSourceArchived),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            actorUserId,
            deviceId,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options)
        );

        await _store.AppendAsync(env, ct);
    }
}

public sealed class GetIncomeSourcesHandler
{
    private readonly IEventStore _store;

    public GetIncomeSourcesHandler(IEventStore store) => _store = store;

    public async Task<IReadOnlyList<IncomeSourceDto>> HandleAsync(CancellationToken ct)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = IncomeSourceProjector.Project(envelopes);

        return state.Sources.Values
            .OrderBy(s => s.IsArchived)
            .ThenBy(s => s.Name)
            .Select(s => new IncomeSourceDto(
                s.SourceId,
                s.Name,
                s.SourceType.ToString(),
                s.CurrencyCode,
                s.IsRecurring,
                s.IsArchived,
                s.TotalReceived,
                s.LastReceivedDate,
                s.CreatedEffectiveDate,
                s.Notes
            ))
            .ToList();
    }
}

public sealed class GetIncomeBySourceReportHandler
{
    private readonly IEventStore _store;

    public GetIncomeBySourceReportHandler(IEventStore store) => _store = store;

    public async Task<IncomeBySourceReportDto> HandleAsync(DateOnly from, DateOnly to, string? currencyCodeFilter, CancellationToken ct)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);

        var sourceState = IncomeSourceProjector.Project(envelopes);
        var cashState = CashLedgerProjector.Project(envelopes);

        // Filter income rows by date range
        var incomeRows = cashState.Rows
            .Where(r => r.Direction == "In" && r.EffectiveDate >= from && r.EffectiveDate <= to)
            .Where(r => r.Category == "Income")
            .ToList();

        if (!string.IsNullOrEmpty(currencyCodeFilter))
            incomeRows = incomeRows.Where(r => r.CurrencyCode.Equals(currencyCodeFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        var perSource = new Dictionary<Guid, (IncomeSourceRecord Source, decimal Total, int Count)>();
        decimal unclassifiedTotal = 0m;
        int unclassifiedCount = 0;

        foreach (var row in incomeRows)
        {
            var matched = sourceState.FindByName(row.Reference);
            if (matched != null)
            {
                if (perSource.TryGetValue(matched.SourceId, out var existing))
                {
                    perSource[matched.SourceId] = (existing.Source, existing.Total + row.Amount, existing.Count + 1);
                }
                else
                {
                    perSource[matched.SourceId] = (matched, row.Amount, 1);
                }
            }
            else
            {
                unclassifiedTotal += row.Amount;
                unclassifiedCount++;
            }
        }

        var rows = perSource.Values
            .OrderByDescending(x => x.Total)
            .Select(x => new IncomeBySourceRow(
                x.Source.SourceId,
                x.Source.Name,
                x.Source.SourceType.ToString(),
                x.Total,
                x.Source.CurrencyCode,
                x.Count
            ))
            .ToList();

        var unclassified = new IncomeBySourceRow(
            null,
            "Unclassified",
            "Other",
            unclassifiedTotal,
            currencyCodeFilter ?? "EGP",
            unclassifiedCount
        );

        var grandTotal = rows.Sum(r => r.Total) + unclassifiedTotal;

        return new IncomeBySourceReportDto(rows, unclassified, grandTotal);
    }
}
