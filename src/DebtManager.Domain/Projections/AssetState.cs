namespace DebtManager.Domain.Projections;

/// <summary>
/// Projected state of a single asset.
/// </summary>
public sealed class AssetRecord
{
    public Guid AssetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string NativeCurrencyCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string QuantityUnit { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string[] Tags { get; set; } = [];
    public string Notes { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public DateOnly CreatedDate { get; set; }
}

/// <summary>
/// A single price point for an asset.
/// </summary>
public sealed class AssetPricePoint
{
    public Guid PriceId { get; set; }
    public Guid AssetId { get; set; }
    public DateOnly AsOfDate { get; set; }
    public decimal PriceAmount { get; set; }
    public string PriceCurrencyCode { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// A single FX rate point.
/// </summary>
public sealed class FxRatePoint
{
    public Guid RateId { get; set; }
    public string FromCurrencyCode { get; set; } = string.Empty;
    public string ToCurrencyCode { get; set; } = string.Empty;
    public DateOnly AsOfDate { get; set; }
    public decimal Rate { get; set; }
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// Full assets state derived deterministically from events.
/// </summary>
public sealed class AssetsState
{
    public Dictionary<Guid, AssetRecord> Assets { get; } = new();
    public List<AssetPricePoint> Prices { get; } = new();
    public List<FxRatePoint> FxRates { get; } = new();
}
