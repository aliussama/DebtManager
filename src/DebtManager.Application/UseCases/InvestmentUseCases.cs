using System.Text.Json;
using DebtManager.Application.Projections;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Commands ---

public sealed record CreateInvestmentAccountCommand(
    Guid? AccountId,
    string Name,
    string CurrencyCode,
    string BrokerName,
    DateOnly EffectiveDate
);

public sealed record ArchiveInvestmentAccountCommand(Guid AccountId, DateOnly EffectiveDate, string Reason);

public sealed record RecordInvestmentTransactionCommand(
    Guid? TransactionId,
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
    string ExternalReference
);

public sealed record ReverseInvestmentTransactionCommand(
    Guid OriginalTransactionId,
    DateOnly EffectiveDate,
    string Reason
);

public sealed record SetCostBasisModeCommand(Guid AccountId, string Mode, DateOnly EffectiveDate);

// --- DTOs ---

public sealed record InvestmentAccountDto(
    Guid AccountId,
    string Name,
    string CurrencyCode,
    string BrokerName,
    bool IsArchived,
    string CostBasisMode,
    decimal CashBalance,
    DateOnly CreatedDate
);

public sealed record InvestmentPositionDto(
    Guid AccountId,
    Guid AssetId,
    string Symbol,
    decimal Quantity,
    decimal AvgCost,
    decimal TotalCost,
    decimal? MarketPrice,
    decimal? MarketValue,
    decimal? UnrealizedPnL,
    bool IsValued
);

public sealed record InvestmentPortfolioDashboardDto(
    IReadOnlyList<InvestmentAccountDto> Accounts,
    IReadOnlyList<InvestmentPositionDto> Positions,
    decimal TotalRealizedPnL,
    decimal TotalUnrealizedPnL,
    decimal TotalMarketValue,
    decimal TotalCostBasis,
    int UnvaluedPositionCount
);

public sealed record HoldingDetailDto(
    Guid AccountId,
    Guid AssetId,
    string Symbol,
    decimal Quantity,
    decimal AvgCost,
    decimal TotalCost,
    IReadOnlyList<InvestmentLotDto> Lots,
    IReadOnlyList<InvestmentTransactionDto> Transactions,
    IReadOnlyList<RealizedPnLEntryDto> RealizedPnLEntries
);

public sealed record InvestmentLotDto(
    Guid TransactionId,
    DateOnly TradeDate,
    decimal RemainingQuantity,
    decimal CostPerUnit
);

public sealed record InvestmentTransactionDto(
    Guid TransactionId,
    Guid AccountId,
    Guid AssetId,
    string Symbol,
    string TransactionType,
    DateOnly TradeDate,
    decimal Quantity,
    decimal PricePerUnit,
    decimal Fees,
    decimal Taxes,
    string CurrencyCode,
    string Notes,
    bool IsReversed
);

public sealed record RealizedPnLEntryDto(
    Guid SellTransactionId,
    DateOnly TradeDate,
    string Symbol,
    decimal QuantitySold,
    decimal Proceeds,
    decimal CostBasis,
    decimal RealizedGain
);

public sealed record InvestmentPerformanceReportDto(
    decimal TotalRealizedPnL,
    decimal TotalUnrealizedPnL,
    decimal NetContributions,
    decimal TotalMarketValue,
    IReadOnlyList<RealizedPnLEntryDto> RealizedEntries
);

// --- Handlers ---

public sealed class CreateInvestmentAccountHandler
{
    private readonly IEventStore _store;
    public CreateInvestmentAccountHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(CreateInvestmentAccountCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var id = cmd.AccountId ?? Guid.NewGuid();
        var ev = new InvestmentAccountCreated(id, cmd.Name, cmd.CurrencyCode, cmd.BrokerName, cmd.EffectiveDate);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(id),
            nameof(InvestmentAccountCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return id;
    }
}

public sealed class ArchiveInvestmentAccountHandler
{
    private readonly IEventStore _store;
    public ArchiveInvestmentAccountHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveInvestmentAccountCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new InvestmentAccountArchived(cmd.AccountId, cmd.EffectiveDate, cmd.Reason);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.AccountId),
            nameof(InvestmentAccountArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class RecordInvestmentTransactionHandler
{
    private readonly IEventStore _store;
    public RecordInvestmentTransactionHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(RecordInvestmentTransactionCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        // Validate transaction type
        var validTypes = new HashSet<string> { "Buy", "Sell", "Dividend", "Interest", "Fee", "Tax", "Split", "TransferIn", "TransferOut" };
        if (!validTypes.Contains(cmd.TransactionType))
            throw new InvalidOperationException($"Invalid transaction type: {cmd.TransactionType}");

        if (cmd.TransactionType == "Split" && cmd.Quantity <= 0)
            throw new InvalidOperationException("Split ratio must be greater than 0.");

        // For sells: validate available quantity
        if (cmd.TransactionType == "Sell" || cmd.TransactionType == "TransferOut")
        {
            var allEnvelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
            var portfolioState = PortfolioProjector.Project(allEnvelopes, cmd.TradeDate);
            var key = (cmd.InvestmentAccountId, cmd.AssetId);
            var available = portfolioState.Positions.TryGetValue(key, out var pos) ? pos.Quantity : 0m;

            if (cmd.Quantity > available)
                throw new InvalidOperationException($"Cannot sell {cmd.Quantity} units. Only {available} available as-of {cmd.TradeDate}.");
        }

        // Idempotency check: ExternalReference + AccountId + TradeDate
        if (!string.IsNullOrEmpty(cmd.ExternalReference))
        {
            var allEnvelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
            var portfolioState = PortfolioProjector.Project(allEnvelopes);
            var duplicate = portfolioState.Transactions.Any(t =>
                !t.IsReversed &&
                t.AccountId == cmd.InvestmentAccountId &&
                t.ExternalReference == cmd.ExternalReference &&
                t.TradeDate == cmd.TradeDate);

            if (duplicate)
                throw new InvalidOperationException($"Duplicate transaction: ExternalReference '{cmd.ExternalReference}' already exists for this account on {cmd.TradeDate}.");
        }

        var txnId = cmd.TransactionId ?? Guid.NewGuid();
        var ev = new InvestmentTransactionRecorded(
            txnId, cmd.InvestmentAccountId, cmd.AssetId, cmd.Symbol,
            cmd.TransactionType, cmd.TradeDate, cmd.SettlementDate,
            cmd.Quantity, cmd.PricePerUnit, cmd.Fees, cmd.Taxes,
            cmd.CurrencyCode, cmd.FxRateToBase, cmd.Notes, cmd.ExternalReference,
            cmd.TradeDate);

        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.InvestmentAccountId),
            nameof(InvestmentTransactionRecorded), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return txnId;
    }
}

public sealed class ReverseInvestmentTransactionHandler
{
    private readonly IEventStore _store;
    public ReverseInvestmentTransactionHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ReverseInvestmentTransactionCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var allEnvelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var portfolioState = PortfolioProjector.Project(allEnvelopes);
        var txn = portfolioState.Transactions.FirstOrDefault(t => t.TransactionId == cmd.OriginalTransactionId);

        if (txn == null)
            throw new InvalidOperationException($"Transaction {cmd.OriginalTransactionId} not found.");

        if (txn.IsReversed)
            throw new InvalidOperationException($"Transaction {cmd.OriginalTransactionId} is already reversed.");

        var reversalId = Guid.NewGuid();
        var ev = new InvestmentTransactionReversed(reversalId, cmd.OriginalTransactionId, cmd.EffectiveDate, cmd.Reason);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(txn.AccountId),
            nameof(InvestmentTransactionReversed), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class SetCostBasisModeHandler
{
    private readonly IEventStore _store;
    public SetCostBasisModeHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(SetCostBasisModeCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        if (cmd.Mode != "FIFO" && cmd.Mode != "AverageCost")
            throw new InvalidOperationException($"Invalid cost basis mode: {cmd.Mode}. Must be 'FIFO' or 'AverageCost'.");

        var ev = new InvestmentCostBasisModeSet(cmd.AccountId, cmd.Mode, cmd.EffectiveDate);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.AccountId),
            nameof(InvestmentCostBasisModeSet), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class GetInvestmentPortfolioDashboardHandler
{
    private readonly IEventStore _store;
    private readonly ProjectionRunner? _runner;
    public GetInvestmentPortfolioDashboardHandler(IEventStore store, ProjectionRunner? runner = null)
    {
        _store = store;
        _runner = runner;
    }

    public async Task<InvestmentPortfolioDashboardDto> HandleAsync(DateOnly? asOfDate = null, CancellationToken ct = default)
    {
        var date = asOfDate ?? DateOnly.FromDateTime(DateTime.Today);

        PortfolioState portfolioState;
        AssetsState assetsState;

        if (_runner != null)
        {
            portfolioState = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.PortfolioState),
                e => PortfolioProjector.Project(e, date),
                asOfDate: date,
                ct: ct);
            assetsState = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.AssetsState),
                e => AssetsProjector.Project(e, date),
                asOfDate: date,
                ct: ct);
        }
        else
        {
            var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
            portfolioState = PortfolioProjector.Project(envelopes, date);
            assetsState = AssetsProjector.Project(envelopes, date);
        }

        var accounts = portfolioState.Accounts.Values
            .Select(a => new InvestmentAccountDto(
                a.AccountId, a.Name, a.CurrencyCode, a.BrokerName,
                a.IsArchived, a.CostBasisMode, Math.Round(a.CashBalance, 2, MidpointRounding.AwayFromZero), a.CreatedDate))
            .ToList();

        var positions = new List<InvestmentPositionDto>();
        decimal totalUnrealized = 0m;
        decimal totalMarketValue = 0m;
        decimal totalCostBasis = 0m;
        int unvaluedCount = 0;

        foreach (var pos in portfolioState.Positions.Values.Where(p => p.Quantity > 0))
        {
            var latestPrice = AssetsProjector.GetLatestPrice(assetsState, pos.AssetId, date);
            bool isValued = latestPrice != null;
            decimal? marketPrice = latestPrice?.PriceAmount;
            decimal? marketValue = isValued ? pos.Quantity * marketPrice!.Value : null;
            decimal? unrealizedPnL = isValued ? PortfolioProjector.ComputeUnrealizedPnL(pos, marketPrice) : null;

            positions.Add(new InvestmentPositionDto(
                pos.AccountId, pos.AssetId, pos.Symbol,
                pos.Quantity,
                Math.Round(pos.AvgCost, 4, MidpointRounding.AwayFromZero),
                Math.Round(pos.TotalCost, 2, MidpointRounding.AwayFromZero),
                marketPrice,
                marketValue.HasValue ? Math.Round(marketValue.Value, 2, MidpointRounding.AwayFromZero) : null,
                unrealizedPnL.HasValue ? Math.Round(unrealizedPnL.Value, 2, MidpointRounding.AwayFromZero) : null,
                isValued));

            if (isValued)
            {
                totalUnrealized += unrealizedPnL!.Value;
                totalMarketValue += marketValue!.Value;
            }
            else
            {
                unvaluedCount++;
            }

            totalCostBasis += pos.TotalCost;
        }

        var totalRealized = portfolioState.RealizedPnL.Sum(r => r.RealizedGain);

        return new InvestmentPortfolioDashboardDto(
            accounts, positions,
            Math.Round(totalRealized, 2, MidpointRounding.AwayFromZero),
            Math.Round(totalUnrealized, 2, MidpointRounding.AwayFromZero),
            Math.Round(totalMarketValue, 2, MidpointRounding.AwayFromZero),
            Math.Round(totalCostBasis, 2, MidpointRounding.AwayFromZero),
            unvaluedCount);
    }
}

public sealed class GetHoldingDetailHandler
{
    private readonly IEventStore _store;
    public GetHoldingDetailHandler(IEventStore store) => _store = store;

    public async Task<HoldingDetailDto?> HandleAsync(Guid accountId, Guid assetId, DateOnly? asOfDate = null, CancellationToken ct = default)
    {
        var date = asOfDate ?? DateOnly.FromDateTime(DateTime.Today);
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var portfolioState = PortfolioProjector.Project(envelopes, date);

        var key = (accountId, assetId);
        if (!portfolioState.Positions.TryGetValue(key, out var pos))
            return null;

        var lots = pos.Lots.Select(l => new InvestmentLotDto(
            l.TransactionId, l.TradeDate, l.RemainingQuantity,
            Math.Round(l.CostPerUnit, 4, MidpointRounding.AwayFromZero))).ToList();

        var transactions = portfolioState.Transactions
            .Where(t => t.AccountId == accountId && t.AssetId == assetId)
            .Select(t => new InvestmentTransactionDto(
                t.TransactionId, t.AccountId, t.AssetId, t.Symbol,
                t.TransactionType, t.TradeDate, t.Quantity, t.PricePerUnit,
                t.Fees, t.Taxes, t.CurrencyCode, t.Notes, t.IsReversed))
            .ToList();

        var pnlEntries = portfolioState.RealizedPnL
            .Where(r => r.AccountId == accountId && r.AssetId == assetId)
            .Select(r => new RealizedPnLEntryDto(
                r.SellTransactionId, r.TradeDate, r.Symbol,
                r.QuantitySold, Math.Round(r.Proceeds, 2, MidpointRounding.AwayFromZero),
                Math.Round(r.CostBasis, 2, MidpointRounding.AwayFromZero),
                Math.Round(r.RealizedGain, 2, MidpointRounding.AwayFromZero)))
            .ToList();

        return new HoldingDetailDto(
            accountId, assetId, pos.Symbol, pos.Quantity,
            Math.Round(pos.AvgCost, 4, MidpointRounding.AwayFromZero),
            Math.Round(pos.TotalCost, 2, MidpointRounding.AwayFromZero),
            lots, transactions, pnlEntries);
    }
}

public sealed class GetPerformanceReportHandler
{
    private readonly IEventStore _store;
    public GetPerformanceReportHandler(IEventStore store) => _store = store;

    public async Task<InvestmentPerformanceReportDto> HandleAsync(DateOnly? asOfDate = null, CancellationToken ct = default)
    {
        var date = asOfDate ?? DateOnly.FromDateTime(DateTime.Today);
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);

        var portfolioState = PortfolioProjector.Project(envelopes, date);
        var assetsState = AssetsProjector.Project(envelopes, date);

        var realizedEntries = portfolioState.RealizedPnL
            .Select(r => new RealizedPnLEntryDto(
                r.SellTransactionId, r.TradeDate, r.Symbol,
                r.QuantitySold, Math.Round(r.Proceeds, 2, MidpointRounding.AwayFromZero),
                Math.Round(r.CostBasis, 2, MidpointRounding.AwayFromZero),
                Math.Round(r.RealizedGain, 2, MidpointRounding.AwayFromZero)))
            .ToList();

        var totalRealized = portfolioState.RealizedPnL.Sum(r => r.RealizedGain);

        decimal totalUnrealized = 0m;
        decimal totalMarketValue = 0m;

        foreach (var pos in portfolioState.Positions.Values.Where(p => p.Quantity > 0))
        {
            var latestPrice = AssetsProjector.GetLatestPrice(assetsState, pos.AssetId, date);
            if (latestPrice != null)
            {
                totalUnrealized += PortfolioProjector.ComputeUnrealizedPnL(pos, latestPrice.PriceAmount);
                totalMarketValue += pos.Quantity * latestPrice.PriceAmount;
            }
        }

        // Net contributions = sum of buy costs - sum of sell proceeds (i.e. net cash outflow)
        var activeTxns = portfolioState.Transactions.Where(t => !t.IsReversed).ToList();
        decimal netContributions = 0m;
        foreach (var txn in activeTxns)
        {
            switch (txn.TransactionType)
            {
                case "Buy":
                case "TransferIn":
                    netContributions += txn.Quantity * txn.PricePerUnit + txn.Fees + txn.Taxes;
                    break;
                case "Sell":
                case "TransferOut":
                    netContributions -= txn.Quantity * txn.PricePerUnit - txn.Fees - txn.Taxes;
                    break;
            }
        }

        return new InvestmentPerformanceReportDto(
            Math.Round(totalRealized, 2, MidpointRounding.AwayFromZero),
            Math.Round(totalUnrealized, 2, MidpointRounding.AwayFromZero),
            Math.Round(netContributions, 2, MidpointRounding.AwayFromZero),
            Math.Round(totalMarketValue, 2, MidpointRounding.AwayFromZero),
            realizedEntries);
    }
}
