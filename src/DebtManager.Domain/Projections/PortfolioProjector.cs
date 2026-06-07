using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

/// <summary>
/// Projects EventEnvelopes into PortfolioState (investment accounts, positions, lots, P&amp;L).
/// Deterministic: same inputs always produce same outputs.
/// Ordering: EffectiveDate ? OccurredAt ? EventId.
/// </summary>
public static class PortfolioProjector
{
    public static PortfolioState Project(IEnumerable<EventEnvelope> envelopes, DateOnly? asOfDate = null)
    {
        var state = new PortfolioState();

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
    /// Computes unrealized P&amp;L for a position given a market price.
    /// </summary>
    public static decimal ComputeUnrealizedPnL(InvestmentPosition position, decimal? marketPrice)
    {
        if (!marketPrice.HasValue || position.Quantity == 0)
            return 0m;

        var marketValue = position.Quantity * marketPrice.Value;
        return marketValue - position.TotalCost;
    }

    private static void Apply(PortfolioState state, EventEnvelope env)
    {
        var opt = DomainJson.Options;

        switch (env.EventType)
        {
            case nameof(InvestmentAccountCreated):
            {
                var ev = JsonSerializer.Deserialize<InvestmentAccountCreated>(env.PayloadJson, opt);
                if (ev == null) return;

                state.Accounts[ev.AccountId] = new InvestmentAccountRecord
                {
                    AccountId = ev.AccountId,
                    Name = ev.Name,
                    CurrencyCode = ev.CurrencyCode,
                    BrokerName = ev.BrokerName,
                    CreatedDate = ev.EffectiveDate
                };
                break;
            }

            case nameof(InvestmentAccountArchived):
            {
                var ev = JsonSerializer.Deserialize<InvestmentAccountArchived>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Accounts.TryGetValue(ev.AccountId, out var account))
                    account.IsArchived = true;
                break;
            }

            case nameof(InvestmentCostBasisModeSet):
            {
                var ev = JsonSerializer.Deserialize<InvestmentCostBasisModeSet>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Accounts.TryGetValue(ev.AccountId, out var account))
                    account.CostBasisMode = ev.Mode;
                break;
            }

            case nameof(InvestmentTransactionRecorded):
            {
                var ev = JsonSerializer.Deserialize<InvestmentTransactionRecorded>(env.PayloadJson, opt);
                if (ev == null) return;

                // Record the transaction for display
                state.Transactions.Add(new InvestmentTransactionRecord
                {
                    TransactionId = ev.TransactionId,
                    AccountId = ev.InvestmentAccountId,
                    AssetId = ev.AssetId,
                    Symbol = ev.Symbol,
                    TransactionType = ev.TransactionType,
                    TradeDate = ev.TradeDate,
                    Quantity = ev.Quantity,
                    PricePerUnit = ev.PricePerUnit,
                    Fees = ev.Fees,
                    Taxes = ev.Taxes,
                    CurrencyCode = ev.CurrencyCode,
                    FxRateToBase = ev.FxRateToBase,
                    Notes = ev.Notes,
                    ExternalReference = ev.ExternalReference
                });

                ApplyTransaction(state, ev);
                break;
            }

            case nameof(InvestmentTransactionReversed):
            {
                var ev = JsonSerializer.Deserialize<InvestmentTransactionReversed>(env.PayloadJson, opt);
                if (ev == null) return;

                state.ReversedTransactionIds.Add(ev.OriginalTransactionId);

                // Mark the transaction as reversed
                var txn = state.Transactions.FirstOrDefault(t => t.TransactionId == ev.OriginalTransactionId);
                if (txn != null)
                    txn.IsReversed = true;

                // Rebuild positions from non-reversed transactions
                RebuildPositions(state);
                break;
            }
        }
    }

    private static void ApplyTransaction(PortfolioState state, InvestmentTransactionRecorded ev)
    {
        var key = (ev.InvestmentAccountId, ev.AssetId);

        // Determine cost basis mode
        var mode = "FIFO";
        if (state.Accounts.TryGetValue(ev.InvestmentAccountId, out var acct))
            mode = acct.CostBasisMode;

        switch (ev.TransactionType)
        {
            case "Buy":
            case "TransferIn":
                ApplyBuy(state, key, ev, mode);
                break;

            case "Sell":
            case "TransferOut":
                ApplySell(state, key, ev, mode);
                break;

            case "Dividend":
            case "Interest":
                ApplyCashImpact(state, ev.InvestmentAccountId, ev.Quantity * ev.PricePerUnit - ev.Fees - ev.Taxes);
                break;

            case "Fee":
                ApplyCashImpact(state, ev.InvestmentAccountId, -(ev.Fees > 0 ? ev.Fees : ev.Quantity * ev.PricePerUnit));
                break;

            case "Tax":
                ApplyCashImpact(state, ev.InvestmentAccountId, -(ev.Taxes > 0 ? ev.Taxes : ev.Quantity * ev.PricePerUnit));
                break;

            case "Split":
                ApplySplit(state, key, ev);
                break;
        }
    }

    private static void ApplyBuy(PortfolioState state, (Guid AccountId, Guid AssetId) key, InvestmentTransactionRecorded ev, string mode)
    {
        if (!state.Positions.TryGetValue(key, out var pos))
        {
            pos = new InvestmentPosition
            {
                AccountId = ev.InvestmentAccountId,
                AssetId = ev.AssetId,
                Symbol = ev.Symbol
            };
            state.Positions[key] = pos;
        }

        var totalCostForTrade = ev.Quantity * ev.PricePerUnit + ev.Fees + ev.Taxes;

        pos.Quantity += ev.Quantity;
        pos.TotalCost += totalCostForTrade;

        // Add lot for FIFO tracking (always track, mode determines sell behavior)
        pos.Lots.Add(new InvestmentLot
        {
            TransactionId = ev.TransactionId,
            TradeDate = ev.TradeDate,
            RemainingQuantity = ev.Quantity,
            CostPerUnit = totalCostForTrade / ev.Quantity
        });

        // Cash impact: buying costs money
        ApplyCashImpact(state, ev.InvestmentAccountId, -totalCostForTrade);
    }

    private static void ApplySell(PortfolioState state, (Guid AccountId, Guid AssetId) key, InvestmentTransactionRecorded ev, string mode)
    {
        if (!state.Positions.TryGetValue(key, out var pos))
            return;

        var proceeds = ev.Quantity * ev.PricePerUnit - ev.Fees - ev.Taxes;
        var quantityToSell = ev.Quantity;
        decimal costBasis;

        if (mode == "AverageCost")
        {
            costBasis = quantityToSell * pos.AvgCost;
            // Reduce lots proportionally (simplified: remove from oldest first)
            var remaining = quantityToSell;
            foreach (var lot in pos.Lots.OrderBy(l => l.TradeDate))
            {
                if (remaining <= 0) break;
                var take = Math.Min(lot.RemainingQuantity, remaining);
                lot.RemainingQuantity -= take;
                remaining -= take;
            }
            pos.Lots.RemoveAll(l => l.RemainingQuantity <= 0);
        }
        else
        {
            // FIFO
            costBasis = 0m;
            var remaining = quantityToSell;
            foreach (var lot in pos.Lots.OrderBy(l => l.TradeDate).ThenBy(l => l.TransactionId))
            {
                if (remaining <= 0) break;
                var take = Math.Min(lot.RemainingQuantity, remaining);
                costBasis += take * lot.CostPerUnit;
                lot.RemainingQuantity -= take;
                remaining -= take;
            }
            pos.Lots.RemoveAll(l => l.RemainingQuantity <= 0);
        }

        pos.Quantity -= quantityToSell;
        pos.TotalCost -= costBasis;

        // Ensure no negative due to rounding
        if (pos.Quantity <= 0)
        {
            pos.Quantity = 0;
            pos.TotalCost = 0;
        }

        state.RealizedPnL.Add(new RealizedPnLEntry
        {
            AccountId = ev.InvestmentAccountId,
            AssetId = ev.AssetId,
            Symbol = ev.Symbol,
            SellTransactionId = ev.TransactionId,
            TradeDate = ev.TradeDate,
            QuantitySold = quantityToSell,
            Proceeds = proceeds,
            CostBasis = costBasis
        });

        // Cash impact: selling brings money in
        ApplyCashImpact(state, ev.InvestmentAccountId, proceeds);
    }

    private static void ApplySplit(PortfolioState state, (Guid AccountId, Guid AssetId) key, InvestmentTransactionRecorded ev)
    {
        if (!state.Positions.TryGetValue(key, out var pos))
            return;

        // Quantity field represents the split ratio (e.g. 2 for 2-for-1)
        var splitRatio = ev.Quantity;
        if (splitRatio <= 0) return;

        // Adjust position: multiply quantity, divide cost per unit
        pos.Quantity *= splitRatio;
        // TotalCost stays the same (value doesn't change, just quantity)

        // Adjust lots
        foreach (var lot in pos.Lots)
        {
            lot.RemainingQuantity *= splitRatio;
            lot.CostPerUnit /= splitRatio;
        }
    }

    private static void ApplyCashImpact(PortfolioState state, Guid accountId, decimal amount)
    {
        if (state.Accounts.TryGetValue(accountId, out var account))
            account.CashBalance += amount;
    }

    /// <summary>
    /// Rebuilds all positions from scratch based on non-reversed transactions.
    /// Called after a reversal to ensure deterministic state.
    /// </summary>
    private static void RebuildPositions(PortfolioState state)
    {
        // Clear current positions and realized P&L
        state.Positions.Clear();
        state.RealizedPnL.Clear();

        // Reset cash balances
        foreach (var acct in state.Accounts.Values)
            acct.CashBalance = 0;

        // Replay all non-reversed transactions in order
        var activeTransactions = state.Transactions
            .Where(t => !state.ReversedTransactionIds.Contains(t.TransactionId))
            .OrderBy(t => t.TradeDate)
            .ThenBy(t => t.TransactionId)
            .ToList();

        foreach (var txn in activeTransactions)
        {
            var ev = new InvestmentTransactionRecorded(
                txn.TransactionId, txn.AccountId, txn.AssetId, txn.Symbol,
                txn.TransactionType, txn.TradeDate, null,
                txn.Quantity, txn.PricePerUnit, txn.Fees, txn.Taxes,
                txn.CurrencyCode, txn.FxRateToBase, txn.Notes, txn.ExternalReference,
                txn.TradeDate);

            ApplyTransaction(state, ev);
        }
    }
}
