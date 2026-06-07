using DebtManager.Domain.Events;
using DebtManager.Domain.Fx;
using DebtManager.Domain.Projections;

namespace DebtManager.Application.Fx;

/// <summary>
/// Centralized FX conversion service.
/// Composes CurrencySettingsProjector + AssetsProjector + FxGraph.
/// Read-only: never writes events.
/// </summary>
public sealed class ReportingCurrencyService
{
    private readonly IEventStore _store;

    public ReportingCurrencyService(IEventStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Convert an amount from one currency to another using the current FX settings.
    /// </summary>
    public async Task<(decimal Value, FxConversionResult Meta)> ConvertAsync(
        decimal amount, string from, string to, DateOnly date, CancellationToken ct)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);

        var settingsState = CurrencySettingsProjector.Project(envelopes);
        var config = new FxPolicyConfig(settingsState.Policy, settingsState.MaxAgeDays);

        var assetsState = AssetsProjector.Project(envelopes, date);
        var graph = FxGraph.Build(assetsState.FxRates);

        if (graph.TryGetRate(from, to, date, config, out var rate, out var meta))
        {
            return (Math.Round(amount * rate, 2, MidpointRounding.AwayFromZero), meta);
        }

        return (0m, meta);
    }

    /// <summary>
    /// Convert using explicit policy config (useful when caller already has config).
    /// </summary>
    public static (decimal Value, FxConversionResult Meta) Convert(
        decimal amount, string from, string to, DateOnly date,
        FxPolicyConfig config, FxGraph graph)
    {
        if (graph.TryGetRate(from, to, date, config, out var rate, out var meta))
        {
            return (Math.Round(amount * rate, 2, MidpointRounding.AwayFromZero), meta);
        }

        return (0m, meta);
    }
}
