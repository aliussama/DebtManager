using System.Text.Json;
using DebtManager.Application.Fx;
using DebtManager.Application.Projections;
using DebtManager.Domain.Events;
using DebtManager.Domain.Fx;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

public sealed record GetNetWorthReportQuery(
    DateOnly AsOfDate,
    string ReportingCurrencyCode = "EGP"
);

public sealed record NetWorthReportDto(
    DateOnly AsOfDate,
    string ReportingCurrency,
    decimal TotalAssets,
    decimal TotalCash,
    decimal TotalInvestmentAssets,
    decimal TotalLiabilities,
    decimal KnownNetWorth,
    int UnknownValueCount,
    IReadOnlyList<NetWorthBreakdownRowDto> Rows
);

public sealed record NetWorthBreakdownRowDto(
    string Category,
    string SubCategory,
    string Name,
    Guid? ReferenceId,
    string NativeCurrencyCode,
    decimal NativeAmount,
    string ReportingCurrencyCode,
    decimal ReportingAmount,
    bool IsValued,
    string ValuationNote
);

public sealed class GetNetWorthReportHandler
{
    private readonly IEventStore _store;
    private readonly GetObligationsListHandler _obligationsHandler;
    private readonly ProjectionRunner? _runner;

    public GetNetWorthReportHandler(IEventStore store, GetObligationsListHandler obligationsHandler, ProjectionRunner? runner = null)
    {
        _store = store;
        _obligationsHandler = obligationsHandler;
        _runner = runner;
    }

    public async Task<NetWorthReportDto> HandleAsync(GetNetWorthReportQuery query, CancellationToken ct)
    {
        var asOf = query.AsOfDate;
        var reportCcy = query.ReportingCurrencyCode;

        CashLedgerState cashState;
        AssetsState assetsState;

        if (_runner != null)
        {
            cashState = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
                e => CashLedgerProjector.Project(e, asOf),
                asOfDate: asOf,
                ct: ct);
            assetsState = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.AssetsState),
                e => AssetsProjector.Project(e, asOf),
                asOfDate: asOf,
                ct: ct);
        }
        else
        {
            var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
            cashState = CashLedgerProjector.Project(envelopes, asOf);
            assetsState = AssetsProjector.Project(envelopes, asOf);
        }

        // 3) Liabilities from obligations
        var obligations = await _obligationsHandler.HandleAsync(asOf, reportCcy, ct);

        var state = new NetWorthState
        {
            AsOfDate = asOf,
            ReportingCurrency = reportCcy
        };

        // --- Cash rows ---
        foreach (var account in cashState.Accounts.Values.Where(a => !a.IsArchived))
        {
            var fxRate = GetFx(assetsState, account.CurrencyCode, reportCcy, asOf);
            var isValued = fxRate.HasValue;
            var reportingAmount = isValued ? account.Balance * fxRate!.Value : 0m;

            state.Rows.Add(new NetWorthBreakdownRow
            {
                Category = "Cash",
                SubCategory = account.AccountType,
                Name = account.Name,
                ReferenceId = account.AccountId,
                NativeCurrencyCode = account.CurrencyCode,
                NativeAmount = account.Balance,
                ReportingCurrencyCode = reportCcy,
                ReportingAmount = Math.Round(reportingAmount, 2, MidpointRounding.AwayFromZero),
                IsValued = isValued,
                ValuationNote = isValued ? string.Empty : $"Missing FX rate {account.CurrencyCode}->{reportCcy}"
            });

            if (isValued)
                state.TotalCash += Math.Round(reportingAmount, 2, MidpointRounding.AwayFromZero);
            else
                state.UnknownValueCount++;
        }

        // --- Asset rows ---
        foreach (var asset in assetsState.Assets.Values.Where(a => !a.IsArchived))
        {
            var latestPrice = AssetsProjector.GetLatestPrice(assetsState, asset.AssetId, asOf);

            if (latestPrice == null)
            {
                state.Rows.Add(new NetWorthBreakdownRow
                {
                    Category = "Asset",
                    SubCategory = asset.AssetType,
                    Name = asset.Name,
                    ReferenceId = asset.AssetId,
                    NativeCurrencyCode = asset.NativeCurrencyCode,
                    NativeAmount = 0m,
                    ReportingCurrencyCode = reportCcy,
                    ReportingAmount = 0m,
                    IsValued = false,
                    ValuationNote = "No price recorded"
                });
                state.UnknownValueCount++;
                continue;
            }

            var nativeValue = asset.Quantity * latestPrice.PriceAmount;
            var priceCcy = latestPrice.PriceCurrencyCode;
            var fxRate = GetFx(assetsState, priceCcy, reportCcy, asOf);
            var isValued = fxRate.HasValue;
            var reportingAmount = isValued ? nativeValue * fxRate!.Value : 0m;

            state.Rows.Add(new NetWorthBreakdownRow
            {
                Category = "Asset",
                SubCategory = asset.AssetType,
                Name = asset.Name,
                ReferenceId = asset.AssetId,
                NativeCurrencyCode = priceCcy,
                NativeAmount = Math.Round(nativeValue, 2, MidpointRounding.AwayFromZero),
                ReportingCurrencyCode = reportCcy,
                ReportingAmount = Math.Round(reportingAmount, 2, MidpointRounding.AwayFromZero),
                IsValued = isValued,
                ValuationNote = isValued ? string.Empty : $"Missing FX rate {priceCcy}->{reportCcy}"
            });

            if (isValued)
                state.TotalInvestmentAssets += Math.Round(reportingAmount, 2, MidpointRounding.AwayFromZero);
            else
                state.UnknownValueCount++;
        }

        // --- Liability rows (obligations outstanding) ---
        foreach (var obl in obligations.Where(o => !o.IsClosed && o.Outstanding > 0))
        {
            var oblCcy = obl.CurrencyCode;
            var fxRate = GetFx(assetsState, oblCcy, reportCcy, asOf);
            var isValued = fxRate.HasValue;
            var reportingAmount = isValued ? obl.Outstanding * fxRate!.Value : 0m;

            state.Rows.Add(new NetWorthBreakdownRow
            {
                Category = "Liability",
                SubCategory = obl.ObligationType,
                Name = obl.Name,
                ReferenceId = obl.ObligationId,
                NativeCurrencyCode = oblCcy,
                NativeAmount = obl.Outstanding,
                ReportingCurrencyCode = reportCcy,
                ReportingAmount = Math.Round(reportingAmount, 2, MidpointRounding.AwayFromZero),
                IsValued = isValued,
                ValuationNote = isValued ? string.Empty : $"Missing FX rate {oblCcy}->{reportCcy}"
            });

            if (isValued)
                state.TotalLiabilities += Math.Round(reportingAmount, 2, MidpointRounding.AwayFromZero);
            else
                state.UnknownValueCount++;
        }

        state.TotalAssets = state.TotalCash + state.TotalInvestmentAssets;
        state.KnownNetWorth = state.TotalAssets - state.TotalLiabilities;

        var rowDtos = state.Rows.Select(r => new NetWorthBreakdownRowDto(
            r.Category, r.SubCategory, r.Name, r.ReferenceId,
            r.NativeCurrencyCode, r.NativeAmount,
            r.ReportingCurrencyCode, r.ReportingAmount,
            r.IsValued, r.ValuationNote
        )).ToList();

        return new NetWorthReportDto(
            state.AsOfDate, state.ReportingCurrency,
            state.TotalAssets, state.TotalCash, state.TotalInvestmentAssets,
            state.TotalLiabilities, state.KnownNetWorth, state.UnknownValueCount,
            rowDtos
        );
    }

    private static decimal? GetFx(AssetsState state, string from, string to, DateOnly asOf)
    {
        return AssetsProjector.GetFxRate(state, from, to, asOf);
    }

    /// <summary>
    /// FxGraph-backed conversion with policy support. Falls back to legacy GetFxRate if graph fails.
    /// </summary>
    internal static decimal? GetFxWithGraph(FxGraph graph, FxPolicyConfig config,
        AssetsState state, string from, string to, DateOnly asOf)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return 1m;

        if (graph.TryGetRate(from, to, asOf, config, out var rate, out _))
            return rate;

        // Legacy fallback
        return AssetsProjector.GetFxRate(state, from, to, asOf);
    }
}
