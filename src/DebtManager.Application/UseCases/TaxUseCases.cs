using System.IO;
using System.Text.Json;
using DebtManager.Application.Fx;
using DebtManager.Application.Projections;
using DebtManager.Domain.Events;
using DebtManager.Domain.Fx;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Tax;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Commands ---

public sealed record CreateTaxProfileCommand(
    Guid? ProfileId, string Name, string CountryCode,
    int TaxYearStartMonth, int TaxYearStartDay,
    string BaseCurrencyCode, DateOnly EffectiveDate);

public sealed record ModifyTaxProfileCommand(
    Guid ProfileId, string? Name, string? CountryCode,
    int? TaxYearStartMonth, int? TaxYearStartDay,
    string? BaseCurrencyCode, DateOnly EffectiveDate);

public sealed record ArchiveTaxProfileCommand(Guid ProfileId, DateOnly EffectiveDate, string Reason);

public sealed record DefineTaxRuleCommand(
    Guid? RuleId, string AppliesTo, string MatchValue,
    string TaxCategory, DateOnly EffectiveDate);

public sealed record ArchiveTaxRuleCommand(Guid RuleId, DateOnly EffectiveDate, string Reason);

public sealed record ConfirmTaxClassificationCommand(
    Guid? ClassificationId, string SourceType, string SourceId,
    string TaxCategory, string Notes, DateOnly EffectiveDate);

// --- DTOs ---

public sealed record TaxProfileDto(
    Guid ProfileId, string Name, string CountryCode,
    int TaxYearStartMonth, int TaxYearStartDay,
    string BaseCurrencyCode, bool IsArchived, DateOnly CreatedDate);

public sealed record TaxRuleDto(
    Guid RuleId, string AppliesTo, string MatchValue,
    string TaxCategory, bool IsArchived);

public sealed record CapitalGainLineDto(
    Guid SellTransactionId, string Symbol, DateOnly TradeDate,
    decimal QuantitySold, decimal Proceeds, decimal CostBasis,
    decimal Fees, decimal Taxes, decimal RealizedGain,
    int HoldingPeriodDays, string CurrencyCode,
    decimal? AmountInBaseCurrency, bool IsValued, string Note);

public sealed record IncomeLineDto(
    string SourceId, string SourceType, string Symbol,
    string IncomeType, DateOnly Date, decimal Amount,
    string CurrencyCode, decimal? AmountInBaseCurrency,
    bool IsValued, string Note);

public sealed record DeductionLineDto(
    string SourceId, string Category, DateOnly Date,
    decimal Amount, string CurrencyCode,
    decimal? AmountInBaseCurrency, bool IsValued, string Note);

public sealed record UnclassifiedLineDto(
    string SourceId, string SourceType, string Description,
    DateOnly Date, decimal Amount, string CurrencyCode);

public sealed record TaxYearReportDto(
    Guid ProfileId, string ProfileName, int TaxYear,
    DateOnly PeriodStart, DateOnly PeriodEnd, string BaseCurrency,
    decimal TotalCapitalGains, decimal TotalDividendIncome,
    decimal TotalInterestIncome, decimal TotalOtherIncome,
    decimal TotalDeductions, int UnclassifiedCount,
    int UnknownValueCount,
    IReadOnlyList<CapitalGainLineDto> CapitalGains,
    IReadOnlyList<IncomeLineDto> IncomeLines,
    IReadOnlyList<DeductionLineDto> Deductions,
    IReadOnlyList<UnclassifiedLineDto> UnclassifiedItems);

// --- Handlers ---

public sealed class CreateTaxProfileHandler
{
    private readonly IEventStore _store;
    public CreateTaxProfileHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(CreateTaxProfileCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var id = cmd.ProfileId ?? Guid.NewGuid();
        var ev = new TaxProfileCreated(id, cmd.Name, cmd.CountryCode,
            cmd.TaxYearStartMonth, cmd.TaxYearStartDay, cmd.BaseCurrencyCode, cmd.EffectiveDate);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(id),
            nameof(TaxProfileCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return id;
    }
}

public sealed class ModifyTaxProfileHandler
{
    private readonly IEventStore _store;
    public ModifyTaxProfileHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ModifyTaxProfileCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new TaxProfileModified(cmd.ProfileId, cmd.Name, cmd.CountryCode,
            cmd.TaxYearStartMonth, cmd.TaxYearStartDay, cmd.BaseCurrencyCode, cmd.EffectiveDate);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.ProfileId),
            nameof(TaxProfileModified), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class ArchiveTaxProfileHandler
{
    private readonly IEventStore _store;
    public ArchiveTaxProfileHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveTaxProfileCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new TaxProfileArchived(cmd.ProfileId, cmd.EffectiveDate, cmd.Reason);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.ProfileId),
            nameof(TaxProfileArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class DefineTaxRuleHandler
{
    private readonly IEventStore _store;
    public DefineTaxRuleHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(DefineTaxRuleCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var id = cmd.RuleId ?? Guid.NewGuid();
        var ev = new TaxRuleDefined(id, cmd.AppliesTo, cmd.MatchValue, cmd.TaxCategory, cmd.EffectiveDate);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(id),
            nameof(TaxRuleDefined), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return id;
    }
}

public sealed class ArchiveTaxRuleHandler
{
    private readonly IEventStore _store;
    public ArchiveTaxRuleHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveTaxRuleCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new TaxRuleArchived(cmd.RuleId, cmd.EffectiveDate, cmd.Reason);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.RuleId),
            nameof(TaxRuleArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class ConfirmTaxClassificationHandler
{
    private readonly IEventStore _store;
    public ConfirmTaxClassificationHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(ConfirmTaxClassificationCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var id = cmd.ClassificationId ?? Guid.NewGuid();
        var ev = new TaxConfirmClassification(id, cmd.SourceType, cmd.SourceId, cmd.TaxCategory, cmd.Notes, cmd.EffectiveDate);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(id),
            nameof(TaxConfirmClassification), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return id;
    }
}

public sealed class GetTaxProfilesHandler
{
    private readonly IEventStore _store;
    public GetTaxProfilesHandler(IEventStore store) => _store = store;

    public async Task<IReadOnlyList<TaxProfileDto>> HandleAsync(CancellationToken ct = default)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = TaxProjector.Project(envelopes);
        return state.Profiles.Values
            .Select(p => new TaxProfileDto(p.ProfileId, p.Name, p.CountryCode,
                p.TaxYearStartMonth, p.TaxYearStartDay, p.BaseCurrencyCode,
                p.IsArchived, p.CreatedDate))
            .ToList();
    }
}

public sealed class GetTaxRulesHandler
{
    private readonly IEventStore _store;
    public GetTaxRulesHandler(IEventStore store) => _store = store;

    public async Task<IReadOnlyList<TaxRuleDto>> HandleAsync(CancellationToken ct = default)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = TaxProjector.Project(envelopes);
        return state.AllRules
            .Select(r => new TaxRuleDto(r.RuleId, r.AppliesTo, r.MatchValue, r.TaxCategory, r.IsArchived))
            .ToList();
    }
}

public sealed class GetTaxYearReportHandler
{
    private readonly IEventStore _store;
    private readonly ProjectionRunner? _runner;
    public GetTaxYearReportHandler(IEventStore store, ProjectionRunner? runner = null)
    {
        _store = store;
        _runner = runner;
    }

    public async Task<TaxYearReportDto> HandleAsync(Guid profileId, int taxYear, CancellationToken ct = default)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);

        var taxState = TaxProjector.Project(envelopes);
        if (!taxState.Profiles.TryGetValue(profileId, out var profile))
            throw new InvalidOperationException($"Tax profile {profileId} not found.");

        var (periodStart, periodEnd) = TaxYear.GetRange(taxYear, profile.TaxYearStartMonth, profile.TaxYearStartDay);
        var baseCcy = profile.BaseCurrencyCode;

        CashLedgerState cashState;
        PortfolioState portfolioState;
        AssetsState assetsState;

        if (_runner != null)
        {
            cashState = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
                e => CashLedgerProjector.Project(e, periodEnd),
                asOfDate: periodEnd,
                ct: ct);
            portfolioState = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.PortfolioState),
                e => PortfolioProjector.Project(e, periodEnd),
                asOfDate: periodEnd,
                ct: ct);
            assetsState = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.AssetsState),
                e => AssetsProjector.Project(e, periodEnd),
                asOfDate: periodEnd,
                ct: ct);
        }
        else
        {
            cashState = CashLedgerProjector.Project(envelopes, periodEnd);
            portfolioState = PortfolioProjector.Project(envelopes, periodEnd);
            assetsState = AssetsProjector.Project(envelopes, periodEnd);
        }

        var capitalGains = new List<CapitalGainLineDto>();
        var incomeLines = new List<IncomeLineDto>();
        var deductions = new List<DeductionLineDto>();
        var unclassified = new List<UnclassifiedLineDto>();
        int unknownCount = 0;

        // --- Capital Gains from investment sells ---
        foreach (var pnl in portfolioState.RealizedPnL)
        {
            if (pnl.TradeDate < periodStart || pnl.TradeDate > periodEnd) continue;

            var txn = portfolioState.Transactions.FirstOrDefault(t => t.TransactionId == pnl.SellTransactionId);
            if (txn == null || txn.IsReversed) continue;

            var classification = TaxClassifier.ClassifyInvestmentTransaction(
                pnl.SellTransactionId.ToString(), txn.TransactionType, pnl.Symbol, taxState);

            if (classification.TaxCategory == TaxCategories.Unclassified)
            {
                unclassified.Add(new UnclassifiedLineDto(
                    pnl.SellTransactionId.ToString(), "Investment",
                    $"Sell {pnl.QuantitySold} {pnl.Symbol}",
                    pnl.TradeDate, pnl.RealizedGain, txn.CurrencyCode));
                continue;
            }

            // Compute holding period from earliest matching buy lot
            var holdingDays = 0;
            var buyTxn = portfolioState.Transactions
                .Where(t => !t.IsReversed && t.AssetId == pnl.AssetId &&
                       t.AccountId == pnl.AccountId &&
                       (t.TransactionType == "Buy" || t.TransactionType == "TransferIn") &&
                       t.TradeDate <= pnl.TradeDate)
                .OrderBy(t => t.TradeDate)
                .FirstOrDefault();
            if (buyTxn != null)
                holdingDays = pnl.TradeDate.DayNumber - buyTxn.TradeDate.DayNumber;

            var fxRate = GetFx(assetsState, txn.CurrencyCode, baseCcy, pnl.TradeDate);
            var isValued = fxRate.HasValue;
            if (!isValued) unknownCount++;

            capitalGains.Add(new CapitalGainLineDto(
                pnl.SellTransactionId, pnl.Symbol, pnl.TradeDate,
                pnl.QuantitySold,
                Math.Round(pnl.Proceeds, 2, MidpointRounding.AwayFromZero),
                Math.Round(pnl.CostBasis, 2, MidpointRounding.AwayFromZero),
                txn.Fees, txn.Taxes,
                Math.Round(pnl.RealizedGain, 2, MidpointRounding.AwayFromZero),
                holdingDays, txn.CurrencyCode,
                isValued ? Math.Round(pnl.RealizedGain * fxRate!.Value, 2, MidpointRounding.AwayFromZero) : null,
                isValued, isValued ? string.Empty : $"Missing FX {txn.CurrencyCode}->{baseCcy}"));
        }

        // --- Dividend/Interest income from investments ---
        foreach (var txn in portfolioState.Transactions.Where(t => !t.IsReversed))
        {
            if (txn.TradeDate < periodStart || txn.TradeDate > periodEnd) continue;
            if (txn.TransactionType != "Dividend" && txn.TransactionType != "Interest") continue;

            var classification = TaxClassifier.ClassifyInvestmentTransaction(
                txn.TransactionId.ToString(), txn.TransactionType, txn.Symbol, taxState);

            var amount = txn.Quantity * txn.PricePerUnit - txn.Fees - txn.Taxes;
            var fxRate = GetFx(assetsState, txn.CurrencyCode, baseCcy, txn.TradeDate);
            var isValued = fxRate.HasValue;
            if (!isValued) unknownCount++;

            if (classification.TaxCategory == TaxCategories.Unclassified)
            {
                unclassified.Add(new UnclassifiedLineDto(
                    txn.TransactionId.ToString(), "Investment",
                    $"{txn.TransactionType} {txn.Symbol}",
                    txn.TradeDate, amount, txn.CurrencyCode));
                continue;
            }

            incomeLines.Add(new IncomeLineDto(
                txn.TransactionId.ToString(), "Investment", txn.Symbol,
                classification.TaxCategory, txn.TradeDate,
                Math.Round(amount, 2, MidpointRounding.AwayFromZero),
                txn.CurrencyCode,
                isValued ? Math.Round(amount * fxRate!.Value, 2, MidpointRounding.AwayFromZero) : null,
                isValued, isValued ? string.Empty : $"Missing FX {txn.CurrencyCode}->{baseCcy}"));
        }

        // --- Cash ledger items ---
        // Track reversed event IDs from cash reversals
        var reversedCashEventIds = new HashSet<Guid>();
        foreach (var row in cashState.Rows)
        {
            if (row.Category == "Income Reversal" || row.Category == "Expense Reversal")
            {
                // Amount is negative for reversals; the CorrelationId might link to original
                // We skip reversal rows from tax reporting; they net out in totals already
                continue;
            }
        }

        foreach (var row in cashState.Rows)
        {
            if (row.EffectiveDate < periodStart || row.EffectiveDate > periodEnd) continue;
            if (row.Category == "Opening Balance" || row.Category == "Income Reversal"
                || row.Category == "Expense Reversal" || row.Direction == "Transfer")
                continue;
            if (row.Amount < 0) continue; // Already netted reversal

            var classification = TaxClassifier.ClassifyCashItem(
                row.EventId.ToString(), row.Direction, row.Category, row.Reference, taxState);

            var fxRate = GetFx(assetsState, row.CurrencyCode, baseCcy, row.EffectiveDate);
            var isValued = fxRate.HasValue;
            if (!isValued) unknownCount++;

            if (classification.TaxCategory == TaxCategories.Unclassified)
            {
                unclassified.Add(new UnclassifiedLineDto(
                    row.EventId.ToString(), "CashLedger",
                    $"{row.Direction}: {row.Category} - {row.Reference}",
                    row.EffectiveDate, row.Amount, row.CurrencyCode));
                continue;
            }

            if (classification.TaxCategory == TaxCategories.DeductibleExpense)
            {
                deductions.Add(new DeductionLineDto(
                    row.EventId.ToString(), row.Category, row.EffectiveDate,
                    Math.Round(row.Amount, 2, MidpointRounding.AwayFromZero),
                    row.CurrencyCode,
                    isValued ? Math.Round(row.Amount * fxRate!.Value, 2, MidpointRounding.AwayFromZero) : null,
                    isValued, isValued ? string.Empty : $"Missing FX {row.CurrencyCode}->{baseCcy}"));
            }
            else if (classification.TaxCategory == TaxCategories.OtherIncome ||
                     classification.TaxCategory == TaxCategories.DividendIncome ||
                     classification.TaxCategory == TaxCategories.InterestIncome)
            {
                incomeLines.Add(new IncomeLineDto(
                    row.EventId.ToString(), "CashLedger", string.Empty,
                    classification.TaxCategory, row.EffectiveDate,
                    Math.Round(row.Amount, 2, MidpointRounding.AwayFromZero),
                    row.CurrencyCode,
                    isValued ? Math.Round(row.Amount * fxRate!.Value, 2, MidpointRounding.AwayFromZero) : null,
                    isValued, isValued ? string.Empty : $"Missing FX {row.CurrencyCode}->{baseCcy}"));
            }
            else if (classification.TaxCategory == TaxCategories.NonDeductible)
            {
                // Non-deductible: tracked but not included in totals
            }
        }

        var totalCapGains = capitalGains.Where(c => c.IsValued).Sum(c => c.AmountInBaseCurrency ?? 0m);
        var totalDividends = incomeLines.Where(i => i.IsValued && i.IncomeType == TaxCategories.DividendIncome).Sum(i => i.AmountInBaseCurrency ?? 0m);
        var totalInterest = incomeLines.Where(i => i.IsValued && i.IncomeType == TaxCategories.InterestIncome).Sum(i => i.AmountInBaseCurrency ?? 0m);
        var totalOther = incomeLines.Where(i => i.IsValued && i.IncomeType == TaxCategories.OtherIncome).Sum(i => i.AmountInBaseCurrency ?? 0m);
        var totalDeductions = deductions.Where(d => d.IsValued).Sum(d => d.AmountInBaseCurrency ?? 0m);

        return new TaxYearReportDto(
            profileId, profile.Name, taxYear, periodStart, periodEnd, baseCcy,
            Math.Round(totalCapGains, 2, MidpointRounding.AwayFromZero),
            Math.Round(totalDividends, 2, MidpointRounding.AwayFromZero),
            Math.Round(totalInterest, 2, MidpointRounding.AwayFromZero),
            Math.Round(totalOther, 2, MidpointRounding.AwayFromZero),
            Math.Round(totalDeductions, 2, MidpointRounding.AwayFromZero),
            unclassified.Count, unknownCount,
            capitalGains, incomeLines, deductions, unclassified);
    }

    /// <summary>
    /// Exports the tax year report as multi-section CSV to a TextWriter.
    /// </summary>
    public static void WriteCsvReport(TaxYearReportDto report, TextWriter writer)
    {
        // SUMMARY
        writer.WriteLine("[SUMMARY]");
        writer.WriteLine("Field,Value");
        writer.WriteLine($"ProfileName,{Esc(report.ProfileName)}");
        writer.WriteLine($"TaxYear,{report.TaxYear}");
        writer.WriteLine($"PeriodStart,{report.PeriodStart:yyyy-MM-dd}");
        writer.WriteLine($"PeriodEnd,{report.PeriodEnd:yyyy-MM-dd}");
        writer.WriteLine($"BaseCurrency,{report.BaseCurrency}");
        writer.WriteLine($"TotalCapitalGains,{report.TotalCapitalGains}");
        writer.WriteLine($"TotalDividendIncome,{report.TotalDividendIncome}");
        writer.WriteLine($"TotalInterestIncome,{report.TotalInterestIncome}");
        writer.WriteLine($"TotalOtherIncome,{report.TotalOtherIncome}");
        writer.WriteLine($"TotalDeductions,{report.TotalDeductions}");
        writer.WriteLine($"UnclassifiedCount,{report.UnclassifiedCount}");
        writer.WriteLine($"UnknownValueCount,{report.UnknownValueCount}");
        writer.WriteLine();

        // CAPITAL_GAINS
        writer.WriteLine("[CAPITAL_GAINS]");
        writer.WriteLine("SellTransactionId,Symbol,TradeDate,QuantitySold,Proceeds,CostBasis,Fees,Taxes,RealizedGain,HoldingPeriodDays,Currency,AmountInBase,IsValued,Note");
        foreach (var cg in report.CapitalGains)
        {
            writer.WriteLine($"{cg.SellTransactionId},{Esc(cg.Symbol)},{cg.TradeDate:yyyy-MM-dd},{cg.QuantitySold},{cg.Proceeds},{cg.CostBasis},{cg.Fees},{cg.Taxes},{cg.RealizedGain},{cg.HoldingPeriodDays},{cg.CurrencyCode},{cg.AmountInBaseCurrency},{cg.IsValued},{Esc(cg.Note)}");
        }
        writer.WriteLine();

        // INCOME
        writer.WriteLine("[INCOME]");
        writer.WriteLine("SourceId,SourceType,Symbol,IncomeType,Date,Amount,Currency,AmountInBase,IsValued,Note");
        foreach (var inc in report.IncomeLines)
        {
            writer.WriteLine($"{Esc(inc.SourceId)},{Esc(inc.SourceType)},{Esc(inc.Symbol)},{Esc(inc.IncomeType)},{inc.Date:yyyy-MM-dd},{inc.Amount},{inc.CurrencyCode},{inc.AmountInBaseCurrency},{inc.IsValued},{Esc(inc.Note)}");
        }
        writer.WriteLine();

        // DEDUCTIONS
        writer.WriteLine("[DEDUCTIONS]");
        writer.WriteLine("SourceId,Category,Date,Amount,Currency,AmountInBase,IsValued,Note");
        foreach (var ded in report.Deductions)
        {
            writer.WriteLine($"{Esc(ded.SourceId)},{Esc(ded.Category)},{ded.Date:yyyy-MM-dd},{ded.Amount},{ded.CurrencyCode},{ded.AmountInBaseCurrency},{ded.IsValued},{Esc(ded.Note)}");
        }
        writer.WriteLine();

        // UNCLASSIFIED
        writer.WriteLine("[UNCLASSIFIED]");
        writer.WriteLine("SourceId,SourceType,Description,Date,Amount,Currency");
        foreach (var unc in report.UnclassifiedItems)
        {
            writer.WriteLine($"{Esc(unc.SourceId)},{Esc(unc.SourceType)},{Esc(unc.Description)},{unc.Date:yyyy-MM-dd},{unc.Amount},{unc.CurrencyCode}");
        }
    }

    private static decimal? GetFx(AssetsState state, string from, string to, DateOnly asOf)
        => AssetsProjector.GetFxRate(state, from, to, asOf);

    internal static decimal? GetFxWithGraph(FxGraph graph, FxPolicyConfig config,
        AssetsState state, string from, string to, DateOnly asOf)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return 1m;

        if (graph.TryGetRate(from, to, asOf, config, out var rate, out _))
            return rate;

        return AssetsProjector.GetFxRate(state, from, to, asOf);
    }

    private static string Esc(string? v)
    {
        if (string.IsNullOrEmpty(v)) return string.Empty;
        if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        return v;
    }
}
