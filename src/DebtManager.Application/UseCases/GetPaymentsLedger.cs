using DebtManager.Application.Internal;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;
using System.Text.Json;

namespace DebtManager.Application.UseCases;

/// <summary>
/// DTO for allocation attached to a payment.
/// </summary>
public sealed record PaymentAllocationDto(
    Guid InstallmentKey,
    decimal Amount,
    string CurrencyCode
);

/// <summary>
/// DTO for a payment ledger row (PaymentMade or PaymentReversed).
/// </summary>
public sealed record PaymentLedgerRowDto(
    Guid PaymentEventId,
    Guid ObligationId,
    string ObligationName,
    DateOnly EffectiveDate,
    decimal Amount,
    string CurrencyCode,
    string? Reference,
    bool IsReversal,
    Guid? OriginalPaymentEventId,
    string? Reason,
    IReadOnlyList<PaymentAllocationDto> Allocations,
    bool IsReversed
)
{
    public string TypeDisplay => IsReversal ? "Reversal" : "Payment";
    public string EffectiveDateDisplay => EffectiveDate.ToString("MMM dd, yyyy");
}

/// <summary>
/// Query to get payments ledger.
/// </summary>
public sealed record GetPaymentsLedgerQuery(
    Guid? ObligationId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null
);

/// <summary>
/// Handler to retrieve all payments and reversals as ledger rows.
/// </summary>
public sealed class GetPaymentsLedgerHandler
{
    private readonly IEventStore _eventStore;

    public GetPaymentsLedgerHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task<IReadOnlyList<PaymentLedgerRowDto>> HandleAsync(
        GetPaymentsLedgerQuery query,
        CancellationToken ct = default)
    {
        var rows = new List<PaymentLedgerRowDto>();

        // Get all events to find obligations and payments
        var allEvents = await _eventStore.ReadAllAsync(
            new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ct
        );

        // Build obligation name lookup
        var obligationNames = new Dictionary<Guid, string>();
        foreach (var envelope in allEvents.Where(e => e.EventType == nameof(ObligationCreated)))
        {
            try
            {
                var created = JsonSerializer.Deserialize<ObligationCreated>(
                    envelope.PayloadJson, DomainJson.Options);
                if (created != null)
                {
                    obligationNames[created.ObligationId] = created.Name;
                }
            }
            catch { }
        }

        // Filter by obligation if specified
        var targetObligationIds = query.ObligationId.HasValue
            ? new HashSet<Guid> { query.ObligationId.Value }
            : obligationNames.Keys.ToHashSet();

        // Track reversed payment IDs
        var reversedPaymentIds = new HashSet<Guid>();
        foreach (var envelope in allEvents.Where(e => e.EventType == nameof(PaymentReversed)))
        {
            try
            {
                var reversed = JsonSerializer.Deserialize<PaymentReversed>(
                    envelope.PayloadJson, DomainJson.Options);
                if (reversed != null)
                {
                    reversedPaymentIds.Add(reversed.OriginalPaymentEventId);
                }
            }
            catch { }
        }

        // Build allocation lookup (PaymentEventId -> allocations)
        var allocationsByPayment = new Dictionary<Guid, List<PaymentAllocationDto>>();
        foreach (var envelope in allEvents.Where(e => e.EventType == nameof(PaymentAllocated)))
        {
            try
            {
                var allocated = JsonSerializer.Deserialize<PaymentAllocated>(
                    envelope.PayloadJson, DomainJson.Options);
                if (allocated != null)
                {
                    if (!allocationsByPayment.ContainsKey(allocated.PaymentEventId))
                        allocationsByPayment[allocated.PaymentEventId] = new List<PaymentAllocationDto>();

                    allocationsByPayment[allocated.PaymentEventId].Add(new PaymentAllocationDto(
                        InstallmentKey: allocated.InstallmentKey,
                        Amount: allocated.Amount.Amount,
                        CurrencyCode: allocated.Amount.Currency.Code
                    ));
                }
            }
            catch { }
        }

        // Process PaymentMade events
        foreach (var envelope in allEvents.Where(e => e.EventType == nameof(PaymentMade)))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<StoredPaymentMade>(
                    envelope.PayloadJson, DomainJson.Options);
                if (stored == null) continue;

                var payment = stored.Payment;
                if (!targetObligationIds.Contains(payment.ObligationId))
                    continue;

                // Apply date filters
                if (query.FromDate.HasValue && payment.EffectiveDate < query.FromDate.Value)
                    continue;
                if (query.ToDate.HasValue && payment.EffectiveDate > query.ToDate.Value)
                    continue;

                var allocations = allocationsByPayment.TryGetValue(stored.PaymentEventId, out var allocs)
                    ? allocs
                    : new List<PaymentAllocationDto>();

                var obligationName = obligationNames.TryGetValue(payment.ObligationId, out var name)
                    ? name
                    : "Unknown";

                rows.Add(new PaymentLedgerRowDto(
                    PaymentEventId: stored.PaymentEventId,
                    ObligationId: payment.ObligationId,
                    ObligationName: obligationName,
                    EffectiveDate: payment.EffectiveDate,
                    Amount: payment.Amount.Amount,
                    CurrencyCode: payment.Amount.Currency.Code,
                    Reference: payment.Reference,
                    IsReversal: false,
                    OriginalPaymentEventId: null,
                    Reason: null,
                    Allocations: allocations,
                    IsReversed: reversedPaymentIds.Contains(stored.PaymentEventId)
                ));
            }
            catch { }
        }

        // Process PaymentReversed events
        foreach (var envelope in allEvents.Where(e => e.EventType == nameof(PaymentReversed)))
        {
            try
            {
                var reversed = JsonSerializer.Deserialize<PaymentReversed>(
                    envelope.PayloadJson, DomainJson.Options);
                if (reversed == null) continue;

                if (!targetObligationIds.Contains(reversed.ObligationId))
                    continue;

                // Apply date filters
                if (query.FromDate.HasValue && reversed.EffectiveDate < query.FromDate.Value)
                    continue;
                if (query.ToDate.HasValue && reversed.EffectiveDate > query.ToDate.Value)
                    continue;

                var obligationName = obligationNames.TryGetValue(reversed.ObligationId, out var name)
                    ? name
                    : "Unknown";

                // Reversals don't have allocations in this row, but we could show reversed allocations
                rows.Add(new PaymentLedgerRowDto(
                    PaymentEventId: envelope.EventId.Value,
                    ObligationId: reversed.ObligationId,
                    ObligationName: obligationName,
                    EffectiveDate: reversed.EffectiveDate,
                    Amount: -reversed.Amount.Amount, // Negative for reversal
                    CurrencyCode: reversed.Amount.Currency.Code,
                    Reference: null,
                    IsReversal: true,
                    OriginalPaymentEventId: reversed.OriginalPaymentEventId,
                    Reason: reversed.Reason,
                    Allocations: Array.Empty<PaymentAllocationDto>(),
                    IsReversed: false // Reversals can't be reversed
                ));
            }
            catch { }
        }

        // Sort by effective date descending (newest first)
        return rows
            .OrderByDescending(r => r.EffectiveDate)
            .ThenByDescending(r => r.PaymentEventId)
            .ToList();
    }
}
