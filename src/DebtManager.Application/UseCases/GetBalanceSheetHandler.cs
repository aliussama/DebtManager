using DebtManager.Application.Fx;
using DebtManager.Application.Projections;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Reporting.Models;
using DebtManager.Reporting.Services;

namespace DebtManager.Application.UseCases;

/// <summary>
/// Use case: Generate Balance Sheet from NetWorthState.
/// No business logic here - only orchestration.
/// Loads NetWorthState and delegates to BalanceSheetGenerator.
/// </summary>
public sealed class GetBalanceSheetHandler
{
    private readonly IEventStore _store;
    private readonly GetObligationsListHandler _obligationsHandler;
    private readonly ProjectionRunner? _runner;

    public GetBalanceSheetHandler(
        IEventStore store,
        GetObligationsListHandler obligationsHandler,
        ProjectionRunner? runner = null)
    {
        _store = store;
        _obligationsHandler = obligationsHandler;
        _runner = runner;
    }

    public async Task<BalanceSheetReport> HandleAsync(
        DateOnly asOfDate,
        string reportingCurrency,
        CancellationToken ct = default)
    {
        // Load NetWorthState using existing GetNetWorthReportHandler logic
        CashLedgerState cashState;
        AssetsState assetsState;

        if (_runner != null)
        {
            cashState = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
                e => CashLedgerProjector.Project(e, asOfDate),
                asOfDate: asOfDate,
                ct: ct);
            assetsState = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.AssetsState),
                e => AssetsProjector.Project(e, asOfDate),
                asOfDate: asOfDate,
                ct: ct);
        }
        else
        {
            var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
            cashState = CashLedgerProjector.Project(envelopes, asOfDate);
            assetsState = AssetsProjector.Project(envelopes, asOfDate);
        }

        // Build NetWorthState (reuse existing logic from GetNetWorthReportHandler)
        var obligations = await _obligationsHandler.HandleAsync(asOfDate, reportingCurrency, ct);

        var netWorthState = new NetWorthState
        {
            AsOfDate = asOfDate,
            ReportingCurrency = reportingCurrency
        };

        // --- Cash rows ---
        foreach (var account in cashState.Accounts.Values.Where(a => !a.IsArchived))
        {
            var fxRate = GetFx(assetsState, account.CurrencyCode, reportingCurrency, asOfDate);
            var isValued = fxRate.HasValue;
            var reportingAmount = isValued ? account.Balance * fxRate!.Value : 0m;

            netWorthState.Rows.Add(new NetWorthBreakdownRow
            {
                Category = "Cash",
                SubCategory = account.AccountType,
                Name = account.Name,
                ReferenceId = account.AccountId,
                NativeCurrencyCode = account.CurrencyCode,
                NativeAmount = account.Balance,
                ReportingCurrencyCode = reportingCurrency,
                ReportingAmount = Math.Round(reportingAmount, 2, MidpointRounding.AwayFromZero),
                IsValued = isValued,
                ValuationNote = isValued ? string.Empty : $"Missing FX rate {account.CurrencyCode}->{reportingCurrency}"
            });

            if (isValued)
                netWorthState.TotalCash += Math.Round(reportingAmount, 2, MidpointRounding.AwayFromZero);
            else
                netWorthState.UnknownValueCount++;
        }

        // --- Asset rows ---
        foreach (var asset in assetsState.Assets.Values.Where(a => !a.IsArchived))
        {
            var latestPrice = AssetsProjector.GetLatestPrice(assetsState, asset.AssetId, asOfDate);

            if (latestPrice == null)
            {
                netWorthState.Rows.Add(new NetWorthBreakdownRow
                {
                    Category = "Asset",
                    SubCategory = asset.AssetType,
                    Name = asset.Name,
                    ReferenceId = asset.AssetId,
                    NativeCurrencyCode = asset.NativeCurrencyCode,
                    NativeAmount = 0m,
                    ReportingCurrencyCode = reportingCurrency,
                    ReportingAmount = 0m,
                    IsValued = false,
                    ValuationNote = "No price recorded"
                });
                netWorthState.UnknownValueCount++;
                continue;
            }

            var nativeValue = asset.Quantity * latestPrice.PriceAmount;
            var priceCcy = latestPrice.PriceCurrencyCode;
            var fxRate = GetFx(assetsState, priceCcy, reportingCurrency, asOfDate);
            var isValued = fxRate.HasValue;
            var reportingAmount = isValued ? nativeValue * fxRate!.Value : 0m;

            netWorthState.Rows.Add(new NetWorthBreakdownRow
            {
                Category = "Asset",
                SubCategory = asset.AssetType,
                Name = asset.Name,
                ReferenceId = asset.AssetId,
                NativeCurrencyCode = priceCcy,
                NativeAmount = Math.Round(nativeValue, 2, MidpointRounding.AwayFromZero),
                ReportingCurrencyCode = reportingCurrency,
                ReportingAmount = Math.Round(reportingAmount, 2, MidpointRounding.AwayFromZero),
                IsValued = isValued,
                ValuationNote = isValued ? string.Empty : $"Missing FX rate {priceCcy}->{reportingCurrency}"
            });

            if (isValued)
                netWorthState.TotalInvestmentAssets += Math.Round(reportingAmount, 2, MidpointRounding.AwayFromZero);
            else
                netWorthState.UnknownValueCount++;
        }

        // --- Liability rows (obligations outstanding) ---
        foreach (var obl in obligations.Where(o => !o.IsClosed && o.Outstanding > 0))
        {
            var oblCcy = obl.CurrencyCode;
            var fxRate = GetFx(assetsState, oblCcy, reportingCurrency, asOfDate);
            var isValued = fxRate.HasValue;
            var reportingAmount = isValued ? obl.Outstanding * fxRate!.Value : 0m;

            netWorthState.Rows.Add(new NetWorthBreakdownRow
            {
                Category = "Liability",
                SubCategory = obl.ObligationType,
                Name = obl.Name,
                ReferenceId = obl.ObligationId,
                NativeCurrencyCode = oblCcy,
                NativeAmount = obl.Outstanding,
                ReportingCurrencyCode = reportingCurrency,
                ReportingAmount = Math.Round(reportingAmount, 2, MidpointRounding.AwayFromZero),
                IsValued = isValued,
                ValuationNote = isValued ? string.Empty : $"Missing FX rate {oblCcy}->{reportingCurrency}"
            });

            if (isValued)
                netWorthState.TotalLiabilities += Math.Round(reportingAmount, 2, MidpointRounding.AwayFromZero);
            else
                netWorthState.UnknownValueCount++;
        }

        netWorthState.TotalAssets = netWorthState.TotalCash + netWorthState.TotalInvestmentAssets;
        netWorthState.KnownNetWorth = netWorthState.TotalAssets - netWorthState.TotalLiabilities;

        // Delegate to BalanceSheetGenerator
        var generator = new BalanceSheetGenerator();
        return generator.Generate(netWorthState, asOfDate, reportingCurrency);
    }

    private static decimal? GetFx(AssetsState state, string from, string to, DateOnly asOf)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return 1m;

        return AssetsProjector.GetFxRate(state, from, to, asOf);
    }
}
