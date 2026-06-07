using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

/// <summary>
/// Projects EventEnvelopes into AssetsState.
/// Deterministic: same inputs always produce same outputs.
/// Ordering: EffectiveDate ? OccurredAt ? EventId.
/// </summary>
public static class AssetsProjector
{
    public static AssetsState Project(IEnumerable<EventEnvelope> envelopes, DateOnly? asOfDate = null)
    {
        var state = new AssetsState();

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value)
            .ToList();

        foreach (var env in ordered)
        {
            if (asOfDate.HasValue && env.EffectiveDate > asOfDate.Value)
                continue;

            Apply(state, env);
        }

        return state;
    }

    /// <summary>
    /// Gets the latest price for an asset as-of the given date, or null if none.
    /// </summary>
    public static AssetPricePoint? GetLatestPrice(AssetsState state, Guid assetId, DateOnly asOfDate)
    {
        return state.Prices
            .Where(p => p.AssetId == assetId && p.AsOfDate <= asOfDate)
            .OrderByDescending(p => p.AsOfDate)
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets the latest FX rate for a currency pair as-of the given date, or null.
    /// Supports direct and inverse lookups.
    /// </summary>
    public static decimal? GetFxRate(AssetsState state, string fromCurrency, string toCurrency, DateOnly asOfDate)
    {
        if (string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase))
            return 1m;

        var direct = state.FxRates
            .Where(r => string.Equals(r.FromCurrencyCode, fromCurrency, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(r.ToCurrencyCode, toCurrency, StringComparison.OrdinalIgnoreCase)
                     && r.AsOfDate <= asOfDate)
            .OrderByDescending(r => r.AsOfDate)
            .FirstOrDefault();

        if (direct != null)
            return direct.Rate;

        var inverse = state.FxRates
            .Where(r => string.Equals(r.FromCurrencyCode, toCurrency, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(r.ToCurrencyCode, fromCurrency, StringComparison.OrdinalIgnoreCase)
                     && r.AsOfDate <= asOfDate)
            .OrderByDescending(r => r.AsOfDate)
            .FirstOrDefault();

        if (inverse != null && inverse.Rate != 0)
            return 1m / inverse.Rate;

        return null;
    }

    private static void Apply(AssetsState state, EventEnvelope env)
    {
        var opt = DomainJson.Options;

        switch (env.EventType)
        {
            case nameof(AssetCreated):
            {
                var ev = JsonSerializer.Deserialize<AssetCreated>(env.PayloadJson, opt);
                if (ev == null) return;

                var (qty, unit, symbol) = ParseQuantitySpec(ev.QuantitySpecJson);
                state.Assets[ev.AssetId] = new AssetRecord
                {
                    AssetId = ev.AssetId,
                    Name = ev.Name,
                    AssetType = ev.AssetType,
                    NativeCurrencyCode = ev.NativeCurrencyCode,
                    Quantity = qty,
                    QuantityUnit = unit,
                    Symbol = symbol,
                    Tags = ev.Tags ?? [],
                    Notes = ev.Notes ?? string.Empty,
                    CreatedDate = ev.EffectiveDate
                };
                break;
            }

            case nameof(AssetUpdatedMetadata):
            {
                var ev = JsonSerializer.Deserialize<AssetUpdatedMetadata>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Assets.TryGetValue(ev.AssetId, out var asset))
                {
                    asset.Name = ev.Name;
                    asset.Tags = ev.Tags ?? [];
                    asset.Notes = ev.Notes ?? string.Empty;
                }
                break;
            }

            case nameof(AssetArchived):
            {
                var ev = JsonSerializer.Deserialize<AssetArchived>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Assets.TryGetValue(ev.AssetId, out var asset))
                    asset.IsArchived = true;
                break;
            }

            case nameof(AssetPriceRecorded):
            {
                var ev = JsonSerializer.Deserialize<AssetPriceRecorded>(env.PayloadJson, opt);
                if (ev == null) return;

                state.Prices.Add(new AssetPricePoint
                {
                    PriceId = ev.PriceId,
                    AssetId = ev.AssetId,
                    AsOfDate = ev.AsOfDate,
                    PriceAmount = ev.PriceAmount,
                    PriceCurrencyCode = ev.PriceCurrencyCode,
                    Source = ev.Source ?? string.Empty
                });
                break;
            }

            case nameof(FxRateRecorded):
            {
                var ev = JsonSerializer.Deserialize<FxRateRecorded>(env.PayloadJson, opt);
                if (ev == null) return;

                state.FxRates.Add(new FxRatePoint
                {
                    RateId = ev.RateId,
                    FromCurrencyCode = ev.FromCurrencyCode,
                    ToCurrencyCode = ev.ToCurrencyCode,
                    AsOfDate = ev.AsOfDate,
                    Rate = ev.Rate,
                    Source = ev.Source ?? string.Empty
                });
                break;
            }

            case nameof(AssetQuantityAdjusted):
            {
                var ev = JsonSerializer.Deserialize<AssetQuantityAdjusted>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Assets.TryGetValue(ev.AssetId, out var asset))
                {
                    var (delta, _, _) = ParseQuantitySpec(ev.DeltaQuantitySpecJson);
                    asset.Quantity += delta;
                }
                break;
            }
        }
    }

    internal static (decimal amount, string unit, string symbol) ParseQuantitySpec(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (0m, string.Empty, string.Empty);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var amount = 0m;
            var unit = string.Empty;
            var symbol = string.Empty;

            if (root.TryGetProperty("amount", out var amtProp))
                amount = amtProp.GetDecimal();
            else if (root.TryGetProperty("units", out var unitsProp))
                amount = unitsProp.GetDecimal();

            if (root.TryGetProperty("unit", out var unitProp))
                unit = unitProp.GetString() ?? string.Empty;

            if (root.TryGetProperty("symbol", out var symProp))
                symbol = symProp.GetString() ?? string.Empty;

            if (root.TryGetProperty("currency", out var currProp))
                unit = currProp.GetString() ?? unit;

            return (amount, unit, symbol);
        }
        catch
        {
            return (0m, string.Empty, string.Empty);
        }
    }
}
