using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Projections.Charges;
using DebtManager.Domain.Rules;
using DebtManager.Domain.Services.Engines;
using DebtManager.Domain.Services.Rules;
using DebtManager.Domain.ValueObjects;
using DebtManager.Reporting.Models;
using DebtManager.Reporting.Services;

namespace DebtManager.Application.Services;

/// <summary>
/// Unified service for obligation management that integrates:
/// - Event sourcing
/// - Rule engine evaluation
/// - Financial projections
/// - Reporting
/// </summary>
public sealed class ObligationManagementService
{
    private readonly IEventStore _eventStore;
    private readonly IRuleEngine _ruleEngine;
    private readonly ObligationSnapshotEngine _snapshotEngine;
    private readonly DashboardGenerator _dashboardGenerator;
    private readonly PaymentProjector _paymentProjector;
    private readonly ChargeReportGenerator _chargeReportGenerator;

    public ObligationManagementService(
        IEventStore eventStore,
        IRuleEngine ruleEngine)
    {
        _eventStore = eventStore;
        _ruleEngine = ruleEngine;
        _snapshotEngine = new ObligationSnapshotEngine(ruleEngine);
        _dashboardGenerator = new DashboardGenerator();
        _paymentProjector = new PaymentProjector();
        _chargeReportGenerator = new ChargeReportGenerator();
    }

    /// <summary>
    /// Get the complete financial snapshot for an obligation as of a specific date.
    /// Includes all rule effects (interest, penalties, grace periods).
    /// </summary>
    public async Task<ObligationFinancialSnapshot> GetObligationSnapshotAsync(
        Guid obligationId,
        DateOnly asOfDate,
        CancellationToken ct = default)
    {
        var currency = Currency.EGP; // TODO: Get from obligation

        // Get obligation stream
        var obligationStream = await _eventStore.ReadStreamAsync(
            new StreamId(obligationId),
            upTo: asOfDate,
            ct
        );

        if (!obligationStream.Any())
            throw new InvalidOperationException($"Obligation {obligationId} not found.");

        // Get all schedule events
        var allEvents = await _eventStore.ReadAllAsync(
            new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ct
        );

        var scheduleEnvelopes = allEvents
            .Where(e => e.EventType.Contains("Schedule", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Compute financial state using rule engine
        var financialState = await _snapshotEngine.ComputeAsync(
            obligationId,
            obligationStream,
            scheduleEnvelopes,
            asOfDate,
            currency,
            ct
        );

        // Extract obligation info
        var createdEnvelope = obligationStream.First(e => e.EventType == nameof(ObligationCreated));
        var created = System.Text.Json.JsonSerializer.Deserialize<ObligationCreated>(
            createdEnvelope.PayloadJson, DomainJson.Options)!;

        var isClosed = obligationStream.Any(e => e.EventType == nameof(ObligationClosed));

        // Build installment snapshots
        var installmentSnapshots = financialState.Installments
            .Select(i => new InstallmentSnapshot(
                InstallmentKey: i.InstallmentKey.ToString() ?? Guid.NewGuid().ToString(),
                DueDate: i.DueDate,
                ExpectedAmount: i.Expected,
                PaidAmount: i.Paid,
                Status: i.Status
            ))
            .ToList();

        // Calculate totals
        var totalPaid = financialState.TotalPayments;
        var totalCharges = SumMoney(financialState.Charges.Select(c => c.Amount), currency);
        var outstandingBalance = created.Principal
            .Add(totalCharges)
            .Subtract(totalPaid);

        return new ObligationFinancialSnapshot(
            ObligationId: obligationId,
            Name: created.Name,
            ObligationType: created.ObligationType,
            Currency: currency,
            AsOfDate: asOfDate,
            Principal: created.Principal,
            TotalPaid: totalPaid,
            TotalCharges: totalCharges,
            OutstandingBalance: outstandingBalance,
            IsClosed: isClosed,
            Installments: installmentSnapshots.AsReadOnly(),
            Charges: financialState.Charges.AsReadOnly(),
            AuditEntries: financialState.Audit.AsReadOnly()
        );
    }

    /// <summary>
    /// Get portfolio dashboard with all obligations.
    /// </summary>
    public async Task<PortfolioDashboard> GetPortfolioDashboardAsync(
        DateOnly asOfDate,
        Currency currency,
        CancellationToken ct = default)
    {
        var obligations = await LoadAllObligationSnapshotsAsync(asOfDate, currency, ct);
        return _dashboardGenerator.Generate(obligations, asOfDate, currency);
    }

    /// <summary>
    /// Get payment projections for cash flow planning.
    /// </summary>
    public async Task<IReadOnlyList<PaymentProjection>> GetPaymentProjectionsAsync(
        DateOnly from,
        DateOnly to,
        Currency currency,
        CancellationToken ct = default)
    {
        var asOfDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var obligations = await LoadAllObligationSnapshotsAsync(to, currency, ct);
        return _paymentProjector.ProjectPayments(obligations, from, to, asOfDate);
    }

    /// <summary>
    /// Get debt payoff projections.
    /// </summary>
    public async Task<IReadOnlyList<DebtPayoffProjection>> GetPayoffProjectionsAsync(
        Currency currency,
        CancellationToken ct = default)
    {
        var asOfDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var obligations = await LoadAllObligationSnapshotsAsync(asOfDate.AddYears(30), currency, ct);
        return _paymentProjector.ProjectPayoffs(obligations);
    }

    /// <summary>
    /// Get charge breakdown report for an obligation.
    /// </summary>
    public async Task<ChargeBreakdownReport> GetChargeBreakdownAsync(
        Guid obligationId,
        DateOnly asOfDate,
        CancellationToken ct = default)
    {
        var snapshot = await GetObligationSnapshotAsync(obligationId, asOfDate, ct);

        var reportingSnapshot = new ObligationSnapshot(
            ObligationId: snapshot.ObligationId,
            Name: snapshot.Name,
            ObligationType: snapshot.ObligationType,
            Currency: snapshot.Currency,
            Principal: snapshot.Principal,
            TotalPaid: snapshot.TotalPaid,
            OutstandingBalance: snapshot.OutstandingBalance,
            IsClosed: snapshot.IsClosed,
            ClosureDate: null,
            Installments: snapshot.Installments,
            Charges: snapshot.Charges
        );

        return _chargeReportGenerator.Generate(reportingSnapshot, asOfDate);
    }

    /// <summary>
    /// Evaluate rules for an installment and get detailed calculation trace.
    /// </summary>
    public async Task<RuleEvaluationResult> EvaluateRulesAsync(
        Guid obligationId,
        Guid installmentKey,
        DateOnly asOfDate,
        CancellationToken ct = default)
    {
        var snapshot = await GetObligationSnapshotAsync(obligationId, asOfDate, ct);

        var installment = snapshot.Installments
            .FirstOrDefault(i => i.InstallmentKey == installmentKey.ToString());

        if (installment == null)
            throw new InvalidOperationException($"Installment {installmentKey} not found.");

        // Build rule context
        var daysOverdue = installment.DueDate < asOfDate
            ? asOfDate.DayNumber - installment.DueDate.DayNumber
            : 0;

        var outstandingAmount = installment.ExpectedAmount.Subtract(installment.PaidAmount);

        var ctx = new RuleEvaluationContext(
            EvaluationDate: asOfDate,
            ObligationId: obligationId,
            InstallmentKey: installmentKey,
            CurrencyCode: snapshot.Currency.Code,
            Facts: new Dictionary<string, object>
            {
                ["installment.is_overdue"] = daysOverdue > 0,
                ["installment.days_overdue"] = daysOverdue,
                ["installment.amount"] = installment.ExpectedAmount.Amount,
                ["installment.paid"] = installment.PaidAmount.Amount,
                ["outstanding.amount"] = outstandingAmount.Amount,
                ["obligation.principal"] = snapshot.Principal.Amount
            }
        );

        var (effects, trace) = await _ruleEngine.EvaluateAsync(ctx, ct);

        return new RuleEvaluationResult(
            ObligationId: obligationId,
            InstallmentKey: installmentKey,
            AsOfDate: asOfDate,
            DaysOverdue: daysOverdue,
            Effects: effects,
            Trace: trace
        );
    }

    private async Task<IReadOnlyList<ObligationSnapshot>> LoadAllObligationSnapshotsAsync(
        DateOnly upTo,
        Currency currency,
        CancellationToken ct)
    {
        var allEvents = await _eventStore.ReadAllAsync(
            new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ct
        );

        var obligationCreatedEvents = allEvents
            .Where(e => e.EventType == nameof(ObligationCreated))
            .ToList();

        var snapshots = new List<ObligationSnapshot>();

        foreach (var envelope in obligationCreatedEvents)
        {
            var created = System.Text.Json.JsonSerializer.Deserialize<ObligationCreated>(
                envelope.PayloadJson, DomainJson.Options);

            if (created == null) continue;

            try
            {
                var fullSnapshot = await GetObligationSnapshotAsync(created.ObligationId, upTo, ct);

                snapshots.Add(new ObligationSnapshot(
                    ObligationId: fullSnapshot.ObligationId,
                    Name: fullSnapshot.Name,
                    ObligationType: fullSnapshot.ObligationType,
                    Currency: currency,
                    Principal: fullSnapshot.Principal,
                    TotalPaid: fullSnapshot.TotalPaid,
                    OutstandingBalance: fullSnapshot.OutstandingBalance,
                    IsClosed: fullSnapshot.IsClosed,
                    ClosureDate: null,
                    Installments: fullSnapshot.Installments,
                    Charges: fullSnapshot.Charges
                ));
            }
            catch
            {
                // Skip obligations that fail to load
            }
        }

        return snapshots.AsReadOnly();
    }

    private static Money SumMoney(IEnumerable<Money> amounts, Currency currency)
    {
        return amounts.Aggregate(Money.Zero(currency), (acc, m) => acc.Add(m));
    }
}

/// <summary>
/// Complete financial snapshot of an obligation.
/// </summary>
public sealed record ObligationFinancialSnapshot(
    Guid ObligationId,
    string Name,
    string ObligationType,
    Currency Currency,
    DateOnly AsOfDate,
    Money Principal,
    Money TotalPaid,
    Money TotalCharges,
    Money OutstandingBalance,
    bool IsClosed,
    IReadOnlyList<InstallmentSnapshot> Installments,
    IReadOnlyList<ComputedCharge> Charges,
    IReadOnlyList<Domain.Audit.AuditEntry> AuditEntries
);

/// <summary>
/// Result of rule evaluation with full trace.
/// </summary>
public sealed record RuleEvaluationResult(
    Guid ObligationId,
    Guid InstallmentKey,
    DateOnly AsOfDate,
    int DaysOverdue,
    IReadOnlyList<RuleEffect> Effects,
    RuleTrace Trace
);
