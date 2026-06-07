using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Commands ---

public sealed record CreateAssetCommand(
    Guid? AssetId,
    string Name,
    string AssetType,
    string NativeCurrencyCode,
    string QuantitySpecJson,
    string[] Tags,
    string Notes,
    DateOnly EffectiveDate
);

public sealed record ArchiveAssetCommand(Guid AssetId, DateOnly EffectiveDate, string Reason);

public sealed record RecordAssetPriceCommand(
    Guid? PriceId,
    Guid AssetId,
    DateOnly AsOfDate,
    decimal PriceAmount,
    string PriceCurrencyCode,
    string Source,
    string Notes
);

public sealed record RecordFxRateCommand(
    Guid? RateId,
    string FromCurrencyCode,
    string ToCurrencyCode,
    DateOnly AsOfDate,
    decimal Rate,
    string Source,
    string Notes
);

public sealed record AdjustAssetQuantityCommand(
    Guid? AdjustmentId,
    Guid AssetId,
    string DeltaQuantitySpecJson,
    DateOnly EffectiveDate,
    string Reason
);

// --- DTOs ---

public sealed record AssetListItemDto(
    Guid AssetId,
    string Name,
    string AssetType,
    string NativeCurrencyCode,
    decimal Quantity,
    string QuantityUnit,
    string Symbol,
    string[] Tags,
    string Notes,
    bool IsArchived,
    decimal? LatestPrice,
    string? LatestPriceCurrency,
    DateOnly? LatestPriceDate
);

// --- Handlers ---

public sealed class CreateAssetHandler
{
    private readonly IEventStore _store;
    public CreateAssetHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(CreateAssetCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var id = cmd.AssetId ?? Guid.NewGuid();
        var ev = new AssetCreated(id, cmd.Name, cmd.AssetType, cmd.NativeCurrencyCode,
            cmd.QuantitySpecJson, cmd.Tags, cmd.Notes, cmd.EffectiveDate);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(id),
            nameof(AssetCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return id;
    }
}

public sealed class ArchiveAssetHandler
{
    private readonly IEventStore _store;
    public ArchiveAssetHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveAssetCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new AssetArchived(cmd.AssetId, cmd.EffectiveDate, cmd.Reason);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.AssetId),
            nameof(AssetArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class RecordAssetPriceHandler
{
    private readonly IEventStore _store;
    public RecordAssetPriceHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(RecordAssetPriceCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var priceId = cmd.PriceId ?? Guid.NewGuid();
        var ev = new AssetPriceRecorded(priceId, cmd.AssetId, cmd.AsOfDate,
            cmd.PriceAmount, cmd.PriceCurrencyCode, cmd.Source, cmd.Notes);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.AssetId),
            nameof(AssetPriceRecorded), DateTimeOffset.UtcNow, ev.AsOfDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return priceId;
    }
}

public sealed class RecordFxRateHandler
{
    private readonly IEventStore _store;
    public RecordFxRateHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(RecordFxRateCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var rateId = cmd.RateId ?? Guid.NewGuid();
        // FX rates use a well-known stream for the pair
        var streamId = DeterministicGuid(cmd.FromCurrencyCode + ":" + cmd.ToCurrencyCode);
        var ev = new FxRateRecorded(rateId, cmd.FromCurrencyCode, cmd.ToCurrencyCode,
            cmd.AsOfDate, cmd.Rate, cmd.Source, cmd.Notes);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(streamId),
            nameof(FxRateRecorded), DateTimeOffset.UtcNow, ev.AsOfDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return rateId;
    }

    private static Guid DeterministicGuid(string key)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(key.ToUpperInvariant());
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return new Guid(hash.AsSpan(0, 16));
    }
}

public sealed class AdjustAssetQuantityHandler
{
    private readonly IEventStore _store;
    public AdjustAssetQuantityHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(AdjustAssetQuantityCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var adjId = cmd.AdjustmentId ?? Guid.NewGuid();
        var ev = new AssetQuantityAdjusted(adjId, cmd.AssetId, cmd.DeltaQuantitySpecJson,
            cmd.EffectiveDate, cmd.Reason);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.AssetId),
            nameof(AssetQuantityAdjusted), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return adjId;
    }
}

public sealed class GetAssetsListHandler
{
    private readonly IEventStore _store;
    public GetAssetsListHandler(IEventStore store) => _store = store;

    public async Task<IReadOnlyList<AssetListItemDto>> HandleAsync(DateOnly? asOfDate = null, CancellationToken ct = default)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var date = asOfDate ?? DateOnly.FromDateTime(DateTime.Today);
        var state = AssetsProjector.Project(envelopes, date);

        return state.Assets.Values
            .Select(a =>
            {
                var price = AssetsProjector.GetLatestPrice(state, a.AssetId, date);
                return new AssetListItemDto(
                    a.AssetId, a.Name, a.AssetType, a.NativeCurrencyCode,
                    a.Quantity, a.QuantityUnit, a.Symbol,
                    a.Tags, a.Notes, a.IsArchived,
                    price?.PriceAmount, price?.PriceCurrencyCode, price?.AsOfDate);
            })
            .ToList();
    }
}
