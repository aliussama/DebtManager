namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: An asset was created.
/// QuantitySpecJson stores type-specific quantity info (e.g., grams, units, property count).
/// </summary>
public sealed record AssetCreated(
    Guid AssetId,
    string Name,
    string AssetType,
    string NativeCurrencyCode,
    string QuantitySpecJson,
    string[] Tags,
    string Notes,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: Asset metadata was updated (name, tags, notes).
/// </summary>
public sealed record AssetUpdatedMetadata(
    Guid AssetId,
    string Name,
    string[] Tags,
    string Notes,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: An asset was archived.
/// </summary>
public sealed record AssetArchived(
    Guid AssetId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A manual price was recorded for an asset.
/// </summary>
public sealed record AssetPriceRecorded(
    Guid PriceId,
    Guid AssetId,
    DateOnly AsOfDate,
    decimal PriceAmount,
    string PriceCurrencyCode,
    string Source,
    string Notes
) : DomainEvent(AsOfDate);

/// <summary>
/// Immutable event: An FX rate was recorded for currency conversion.
/// </summary>
public sealed record FxRateRecorded(
    Guid RateId,
    string FromCurrencyCode,
    string ToCurrencyCode,
    DateOnly AsOfDate,
    decimal Rate,
    string Source,
    string Notes
) : DomainEvent(AsOfDate);

/// <summary>
/// Immutable event: Asset quantity was adjusted (buy, sell, depreciation, etc.).
/// DeltaQuantitySpecJson uses same schema as QuantitySpecJson but with signed delta.
/// </summary>
public sealed record AssetQuantityAdjusted(
    Guid AdjustmentId,
    Guid AssetId,
    string DeltaQuantitySpecJson,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);
