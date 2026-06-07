namespace DebtManager.Domain.Projections;

/// <summary>
/// Projected state of an investment account.
/// </summary>
public sealed class InvestmentAccountRecord
{
    public Guid AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public string BrokerName { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public DateOnly CreatedDate { get; set; }
    public string CostBasisMode { get; set; } = "FIFO";
    public decimal CashBalance { get; set; }
}

/// <summary>
/// A single lot representing a purchase of shares, for FIFO tracking.
/// </summary>
public sealed class InvestmentLot
{
    public Guid TransactionId { get; set; }
    public DateOnly TradeDate { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal CostPerUnit { get; set; }
}

/// <summary>
/// A position in a specific asset within an investment account.
/// </summary>
public sealed class InvestmentPosition
{
    public Guid AccountId { get; set; }
    public Guid AssetId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal TotalCost { get; set; }
    public decimal AvgCost => Quantity != 0 ? TotalCost / Quantity : 0m;
    public List<InvestmentLot> Lots { get; } = new();
}

/// <summary>
/// A realized P&amp;L entry from a sell transaction.
/// </summary>
public sealed class RealizedPnLEntry
{
    public Guid AccountId { get; set; }
    public Guid AssetId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public Guid SellTransactionId { get; set; }
    public DateOnly TradeDate { get; set; }
    public decimal QuantitySold { get; set; }
    public decimal Proceeds { get; set; }
    public decimal CostBasis { get; set; }
    public decimal RealizedGain => Proceeds - CostBasis;
}

/// <summary>
/// A recorded investment transaction (for display purposes).
/// </summary>
public sealed class InvestmentTransactionRecord
{
    public Guid TransactionId { get; set; }
    public Guid AccountId { get; set; }
    public Guid AssetId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public DateOnly TradeDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal PricePerUnit { get; set; }
    public decimal Fees { get; set; }
    public decimal Taxes { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal? FxRateToBase { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string ExternalReference { get; set; } = string.Empty;
    public bool IsReversed { get; set; }
}

/// <summary>
/// Full portfolio state derived deterministically from events.
/// </summary>
public sealed class PortfolioState
{
    public Dictionary<Guid, InvestmentAccountRecord> Accounts { get; } = new();
    public Dictionary<(Guid AccountId, Guid AssetId), InvestmentPosition> Positions { get; } = new();
    public List<RealizedPnLEntry> RealizedPnL { get; } = new();
    public List<InvestmentTransactionRecord> Transactions { get; } = new();
    public HashSet<Guid> ReversedTransactionIds { get; } = new();
}
