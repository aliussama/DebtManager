namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: An investment account was created.
/// </summary>
public sealed record InvestmentAccountCreated(
    Guid AccountId,
    string Name,
    string CurrencyCode,
    string BrokerName,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: An investment account was archived.
/// </summary>
public sealed record InvestmentAccountArchived(
    Guid AccountId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: An investment transaction was recorded.
/// Canonical form for all transaction types: Buy, Sell, Dividend, Interest, Fee, Tax, Split, TransferIn, TransferOut.
/// </summary>
public sealed record InvestmentTransactionRecorded(
    Guid TransactionId,
    Guid InvestmentAccountId,
    Guid AssetId,
    string Symbol,
    string TransactionType,
    DateOnly TradeDate,
    DateOnly? SettlementDate,
    decimal Quantity,
    decimal PricePerUnit,
    decimal Fees,
    decimal Taxes,
    string CurrencyCode,
    decimal? FxRateToBase,
    string Notes,
    string ExternalReference,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: An investment transaction was reversed.
/// </summary>
public sealed record InvestmentTransactionReversed(
    Guid ReversalId,
    Guid OriginalTransactionId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: Cost basis mode was set for an investment account.
/// Mode: "FIFO" or "AverageCost"
/// </summary>
public sealed record InvestmentCostBasisModeSet(
    Guid AccountId,
    string Mode,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
