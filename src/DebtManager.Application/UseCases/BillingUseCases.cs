using System.Text.Json;
using DebtManager.Domain.Billing;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Commands ---

public sealed record IssueBillCommand(
    Guid? BillId, Guid? ContractId, Guid PartyId, string CurrencyCode,
    decimal Amount, DateOnly DueDate, string Category, string Reference,
    string? Notes, DateOnly EffectiveDate);

public sealed record IssueInvoiceCommand(
    Guid? InvoiceId, Guid? ContractId, Guid PartyId, string CurrencyCode,
    decimal Amount, DateOnly DueDate, string Category, string Reference,
    string? Notes, DateOnly EffectiveDate);

public sealed record CancelBillCommand(Guid BillId, string Reason, DateOnly EffectiveDate);
public sealed record CancelInvoiceCommand(Guid InvoiceId, string Reason, DateOnly EffectiveDate);
public sealed record DisputeBillCommand(Guid BillId, string Reason, DateOnly EffectiveDate);
public sealed record DisputeInvoiceCommand(Guid InvoiceId, string Reason, DateOnly EffectiveDate);
public sealed record WriteOffBillCommand(Guid BillId, string Reason, DateOnly EffectiveDate);
public sealed record WriteOffInvoiceCommand(Guid InvoiceId, string Reason, DateOnly EffectiveDate);

public sealed record AddBillAdjustmentCommand(
    Guid BillId, Guid? AdjustmentId, string Kind, decimal Amount, string? Notes, DateOnly EffectiveDate);
public sealed record AddInvoiceAdjustmentCommand(
    Guid InvoiceId, Guid? AdjustmentId, string Kind, decimal Amount, string? Notes, DateOnly EffectiveDate);
public sealed record ReverseBillAdjustmentCommand(Guid BillId, Guid AdjustmentId, string Reason, DateOnly EffectiveDate);
public sealed record ReverseInvoiceAdjustmentCommand(Guid InvoiceId, Guid AdjustmentId, string Reason, DateOnly EffectiveDate);

public sealed record RecordBillPaymentCommand(
    Guid BillId, Guid? PaymentId, Guid AccountId, decimal Amount, string CurrencyCode,
    DateOnly PaidDate, string Method, string? ExternalReference, DateOnly EffectiveDate);
public sealed record RecordInvoicePaymentCommand(
    Guid InvoiceId, Guid? PaymentId, Guid AccountId, decimal Amount, string CurrencyCode,
    DateOnly PaidDate, string Method, string? ExternalReference, DateOnly EffectiveDate);
public sealed record ReverseBillPaymentCommand(Guid BillId, Guid PaymentId, string Reason, DateOnly EffectiveDate);
public sealed record ReverseInvoicePaymentCommand(Guid InvoiceId, Guid PaymentId, string Reason, DateOnly EffectiveDate);
public sealed record UnapplyBillPaymentCommand(Guid BillId, Guid PaymentId, string Reason, DateOnly EffectiveDate);
public sealed record UnapplyInvoicePaymentCommand(Guid InvoiceId, Guid PaymentId, string Reason, DateOnly EffectiveDate);

// --- DTOs ---

public sealed record BillListItemDto(
    Guid BillId, Guid? ContractId, Guid PartyId, string PartyName,
    string CurrencyCode, decimal Amount, DateOnly DueDate,
    string Category, string Reference, string Status,
    decimal Outstanding, decimal TotalPaid);

public sealed record InvoiceListItemDto(
    Guid InvoiceId, Guid? ContractId, Guid PartyId, string PartyName,
    string CurrencyCode, decimal Amount, DateOnly DueDate,
    string Category, string Reference, string Status,
    decimal Outstanding, decimal TotalPaid);

public sealed record BillingDashboardDto(
    IReadOnlyList<BillListItemDto> Bills,
    IReadOnlyList<InvoiceListItemDto> Invoices,
    decimal TotalBillsOutstanding,
    decimal TotalInvoicesOutstanding,
    int OverdueBillsCount,
    int OverdueInvoicesCount);

public sealed record AgingReportRowDto(
    string Bucket, int BillCount, decimal BillAmount,
    int InvoiceCount, decimal InvoiceAmount);

// --- Well-known stream ID for billing events ---
internal static class BillingStreams
{
    public static readonly StreamId BillingStream = new(Guid.Parse("B1001A00-0001-0001-0001-000000000001"));
}

// --- Handlers ---

public sealed class IssueBillHandler
{
    private readonly IEventStore _store;
    public IssueBillHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(IssueBillCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var id = cmd.BillId ?? Guid.NewGuid();
        var ev = new BillIssued(id, cmd.ContractId, cmd.PartyId, cmd.CurrencyCode,
            cmd.Amount, cmd.DueDate, cmd.Category, cmd.Reference, cmd.Notes, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(BillIssued), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
        return id;
    }
}

public sealed class IssueInvoiceHandler
{
    private readonly IEventStore _store;
    public IssueInvoiceHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(IssueInvoiceCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var id = cmd.InvoiceId ?? Guid.NewGuid();
        var ev = new InvoiceIssued(id, cmd.ContractId, cmd.PartyId, cmd.CurrencyCode,
            cmd.Amount, cmd.DueDate, cmd.Category, cmd.Reference, cmd.Notes, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(InvoiceIssued), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
        return id;
    }
}

public sealed class CancelBillHandler
{
    private readonly IEventStore _store;
    public CancelBillHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(CancelBillCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new BillCancelled(cmd.BillId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(BillCancelled), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class CancelInvoiceHandler
{
    private readonly IEventStore _store;
    public CancelInvoiceHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(CancelInvoiceCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new InvoiceCancelled(cmd.InvoiceId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(InvoiceCancelled), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class DisputeBillHandler
{
    private readonly IEventStore _store;
    public DisputeBillHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(DisputeBillCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new BillDisputed(cmd.BillId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(BillDisputed), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class DisputeInvoiceHandler
{
    private readonly IEventStore _store;
    public DisputeInvoiceHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(DisputeInvoiceCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new InvoiceDisputed(cmd.InvoiceId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(InvoiceDisputed), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class WriteOffBillHandler
{
    private readonly IEventStore _store;
    public WriteOffBillHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(WriteOffBillCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new BillWrittenOff(cmd.BillId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(BillWrittenOff), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class WriteOffInvoiceHandler
{
    private readonly IEventStore _store;
    public WriteOffInvoiceHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(WriteOffInvoiceCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new InvoiceWrittenOff(cmd.InvoiceId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(InvoiceWrittenOff), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class AddBillAdjustmentHandler
{
    private readonly IEventStore _store;
    public AddBillAdjustmentHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(AddBillAdjustmentCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var adjId = cmd.AdjustmentId ?? Guid.NewGuid();
        var ev = new BillAdjustmentAdded(cmd.BillId, adjId, cmd.Kind, cmd.Amount, cmd.Notes, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(BillAdjustmentAdded), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
        return adjId;
    }
}

public sealed class AddInvoiceAdjustmentHandler
{
    private readonly IEventStore _store;
    public AddInvoiceAdjustmentHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(AddInvoiceAdjustmentCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var adjId = cmd.AdjustmentId ?? Guid.NewGuid();
        var ev = new InvoiceAdjustmentAdded(cmd.InvoiceId, adjId, cmd.Kind, cmd.Amount, cmd.Notes, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(InvoiceAdjustmentAdded), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
        return adjId;
    }
}

public sealed class ReverseBillAdjustmentHandler
{
    private readonly IEventStore _store;
    public ReverseBillAdjustmentHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ReverseBillAdjustmentCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new BillAdjustmentReversed(cmd.BillId, cmd.AdjustmentId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(BillAdjustmentReversed), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class ReverseInvoiceAdjustmentHandler
{
    private readonly IEventStore _store;
    public ReverseInvoiceAdjustmentHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ReverseInvoiceAdjustmentCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new InvoiceAdjustmentReversed(cmd.InvoiceId, cmd.AdjustmentId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(InvoiceAdjustmentReversed), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class RecordBillPaymentHandler
{
    private readonly IEventStore _store;
    public RecordBillPaymentHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(RecordBillPaymentCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var paymentId = cmd.PaymentId ?? Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        // 1. Record bill payment
        var ev = new BillPaymentRecorded(cmd.BillId, paymentId, cmd.AccountId, cmd.Amount,
            cmd.CurrencyCode, cmd.PaidDate, cmd.Method, cmd.ExternalReference, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(BillPaymentRecorded), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);

        // 2. Also record as expense (cash out)
        var currency = new Currency(cmd.CurrencyCode, 2);
        var expense = new ExpenseRecorded(cmd.AccountId, new Money(cmd.Amount, currency),
            cmd.EffectiveDate, "Bill Payment", $"Bill Payment: {cmd.BillId}");
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.AccountId),
            nameof(ExpenseRecorded), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(expense, DomainJson.Options)), ct);

        return paymentId;
    }
}

public sealed class RecordInvoicePaymentHandler
{
    private readonly IEventStore _store;
    public RecordInvoicePaymentHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(RecordInvoicePaymentCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var paymentId = cmd.PaymentId ?? Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        // 1. Record invoice payment
        var ev = new InvoicePaymentRecorded(cmd.InvoiceId, paymentId, cmd.AccountId, cmd.Amount,
            cmd.CurrencyCode, cmd.PaidDate, cmd.Method, cmd.ExternalReference, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(InvoicePaymentRecorded), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);

        // 2. Also record as income (cash in)
        var currency = new Currency(cmd.CurrencyCode, 2);
        var income = new IncomeRecorded(cmd.AccountId, new Money(cmd.Amount, currency),
            cmd.EffectiveDate, $"Invoice Payment: {cmd.InvoiceId}");
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.AccountId),
            nameof(IncomeRecorded), DateTimeOffset.UtcNow, cmd.EffectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(income, DomainJson.Options)), ct);

        return paymentId;
    }
}

public sealed class ReverseBillPaymentHandler
{
    private readonly IEventStore _store;
    public ReverseBillPaymentHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ReverseBillPaymentCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new BillPaymentReversed(cmd.BillId, cmd.PaymentId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(BillPaymentReversed), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class ReverseInvoicePaymentHandler
{
    private readonly IEventStore _store;
    public ReverseInvoicePaymentHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ReverseInvoicePaymentCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new InvoicePaymentReversed(cmd.InvoiceId, cmd.PaymentId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(InvoicePaymentReversed), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class UnapplyBillPaymentHandler
{
    private readonly IEventStore _store;
    public UnapplyBillPaymentHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(UnapplyBillPaymentCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new BillPaymentUnapplied(cmd.BillId, cmd.PaymentId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(BillPaymentUnapplied), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class UnapplyInvoicePaymentHandler
{
    private readonly IEventStore _store;
    public UnapplyInvoicePaymentHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(UnapplyInvoicePaymentCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new InvoicePaymentUnapplied(cmd.InvoiceId, cmd.PaymentId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(InvoicePaymentUnapplied), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class GetBillingDashboardHandler
{
    private readonly IEventStore _store;
    public GetBillingDashboardHandler(IEventStore store) => _store = store;

    public async Task<BillingDashboardDto> HandleAsync(DateOnly asOfDate, bool includeSettled, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var billingState = BillingProjector.Project(all, asOfDate);
        var partyState = PartiesProjector.Project(all);

        var bills = billingState.Bills.Values
            .Where(b => includeSettled || b.Status is not ("Cancelled" or "WrittenOff" or "Paid"))
            .Select(b =>
            {
                var partyName = partyState.Parties.TryGetValue(b.PartyId, out var p) ? p.Name : "";
                return new BillListItemDto(b.BillId, b.ContractId, b.PartyId, partyName,
                    b.CurrencyCode, b.Amount, b.DueDate, b.Category, b.Reference,
                    BillingStatusRules.IsOverdue(b.DueDate, asOfDate, b.Status) ? "Overdue" : b.Status,
                    b.Outstanding, b.TotalPaid);
            }).ToList();

        var invoices = billingState.Invoices.Values
            .Where(i => includeSettled || i.Status is not ("Cancelled" or "WrittenOff" or "Paid"))
            .Select(i =>
            {
                var partyName = partyState.Parties.TryGetValue(i.PartyId, out var p) ? p.Name : "";
                return new InvoiceListItemDto(i.InvoiceId, i.ContractId, i.PartyId, partyName,
                    i.CurrencyCode, i.Amount, i.DueDate, i.Category, i.Reference,
                    BillingStatusRules.IsOverdue(i.DueDate, asOfDate, i.Status) ? "Overdue" : i.Status,
                    i.Outstanding, i.TotalPaid);
            }).ToList();

        return new BillingDashboardDto(bills, invoices,
            bills.Sum(b => b.Outstanding), invoices.Sum(i => i.Outstanding),
            billingState.OverdueBills(asOfDate).Count,
            billingState.OverdueInvoices(asOfDate).Count);
    }
}

public sealed class GetAgingReportHandler
{
    private readonly IEventStore _store;
    public GetAgingReportHandler(IEventStore store) => _store = store;

    public async Task<IReadOnlyList<AgingReportRowDto>> HandleAsync(DateOnly asOfDate, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var billingState = BillingProjector.Project(all, asOfDate);

        var buckets = new[] { "Current", "0-30", "31-60", "61-90", "90+" };
        var result = new List<AgingReportRowDto>();

        foreach (var bucket in buckets)
        {
            var billsInBucket = billingState.Bills.Values
                .Where(b => b.Status is "Due" or "PartiallyPaid" && BillingStatusRules.AgingBucket(b.DueDate, asOfDate) == bucket)
                .ToList();
            var invoicesInBucket = billingState.Invoices.Values
                .Where(i => i.Status is "Due" or "PartiallyPaid" && BillingStatusRules.AgingBucket(i.DueDate, asOfDate) == bucket)
                .ToList();

            result.Add(new AgingReportRowDto(bucket,
                billsInBucket.Count, billsInBucket.Sum(b => b.Outstanding),
                invoicesInBucket.Count, invoicesInBucket.Sum(i => i.Outstanding)));
        }

        return result;
    }
}
