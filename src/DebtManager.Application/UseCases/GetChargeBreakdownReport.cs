using DebtManager.Domain.Events;
using DebtManager.Domain.Projections.Charges;
using DebtManager.Domain.Rules;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

public sealed record GetChargeBreakdownReportQuery(
    Guid ObligationId,
    DateOnly AsOfDate
);

public sealed record ChargeBreakdownReportDto(
    Guid ObligationId,
    string ObligationName,
    DateOnly AsOfDate,
    IReadOnlyList<ChargeTypeSummaryDto> Summaries,
    IReadOnlyList<ChargeItemDto> Items
);

public sealed record ChargeTypeSummaryDto(
    string ChargeType,
    decimal TotalAssessed,
    decimal TotalPaid,
    decimal Outstanding,
    int Count,
    string CurrencyCode
);

public sealed record ChargeItemDto(
    Guid ChargeId,
    string ChargeType,
    DateOnly EffectiveDate,
    decimal AssessedAmount,
    decimal PaidAmount,
    decimal OutstandingAmount,
    string CurrencyCode,
    Guid? RelatedEventId,
    string? Notes
);

/// <summary>
/// Handler that builds a charge breakdown report for a single obligation.
/// Uses GetFinancialSnapshotHandler to derive charge data — no domain logic here.
/// </summary>
public sealed class GetChargeBreakdownReportHandler
{
    private readonly GetFinancialSnapshotHandler _snapshotHandler;
    private readonly GetObligationsListHandler _obligationsListHandler;

    public GetChargeBreakdownReportHandler(
        GetFinancialSnapshotHandler snapshotHandler,
        GetObligationsListHandler obligationsListHandler)
    {
        _snapshotHandler = snapshotHandler;
        _obligationsListHandler = obligationsListHandler;
    }

    public async Task<ChargeBreakdownReportDto> HandleAsync(
        GetChargeBreakdownReportQuery query,
        CancellationToken ct = default)
    {
        // Get obligation name from the list handler
        var obligations = await _obligationsListHandler.HandleAsync(query.AsOfDate, "EGP", ct);
        var obligation = obligations.FirstOrDefault(o => o.ObligationId == query.ObligationId);
        var obligationName = obligation?.Name ?? "Unknown";

        // Get the financial snapshot which already has charges computed by the domain/rule engine
        var state = await _snapshotHandler.HandleAsync(query.ObligationId, query.AsOfDate, ct);

        var currencyCode = "EGP";

        // Build charge items with paid/outstanding computed from ChargePayments
        var items = state.Charges
            .Select(c =>
            {
                var paidAmount = state.ChargePayments.TryGetValue(c.ChargeId, out var paid)
                    ? paid.Amount
                    : 0m;
                var outstanding = c.Amount.Amount - paidAmount;

                return new ChargeItemDto(
                    ChargeId: c.ChargeId,
                    ChargeType: c.Type.ToString(),
                    EffectiveDate: c.EffectiveDate,
                    AssessedAmount: c.Amount.Amount,
                    PaidAmount: paidAmount,
                    OutstandingAmount: Math.Max(0m, outstanding),
                    CurrencyCode: c.Amount.Currency.Code,
                    RelatedEventId: c.InstallmentKey,
                    Notes: c.Label
                );
            })
            // Stable ordering: EffectiveDate desc, then ChargeType, then ChargeId
            .OrderByDescending(i => i.EffectiveDate)
            .ThenBy(i => i.ChargeType)
            .ThenBy(i => i.ChargeId)
            .ToList();

        // Build summaries grouped by ChargeType
        var summaries = items
            .GroupBy(i => i.ChargeType)
            .Select(g => new ChargeTypeSummaryDto(
                ChargeType: g.Key,
                TotalAssessed: g.Sum(i => i.AssessedAmount),
                TotalPaid: g.Sum(i => i.PaidAmount),
                Outstanding: g.Sum(i => i.OutstandingAmount),
                Count: g.Count(),
                CurrencyCode: g.First().CurrencyCode
            ))
            // Stable ordering: ChargeType ascending
            .OrderBy(s => s.ChargeType)
            .ToList();

        return new ChargeBreakdownReportDto(
            ObligationId: query.ObligationId,
            ObligationName: obligationName,
            AsOfDate: query.AsOfDate,
            Summaries: summaries,
            Items: items
        );
    }
}
