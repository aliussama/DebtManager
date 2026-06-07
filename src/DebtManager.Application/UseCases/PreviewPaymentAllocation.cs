using DebtManager.Application.Internal;
using DebtManager.Domain.Allocation;
using DebtManager.Domain.Events;
using DebtManager.Domain.Installments;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Projections.Installments;
using DebtManager.Domain.Projections.Charges;
using DebtManager.Domain.Rules;
using DebtManager.Domain.Scheduling;
using DebtManager.Domain.Services;
using DebtManager.Domain.Services.Allocation;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

/// <summary>
/// Command to preview payment allocation.
/// </summary>
public sealed record PreviewPaymentAllocationCommand(
    Guid ObligationId,
    decimal Amount,
    string CurrencyCode,
    DateOnly EffectiveDate,
    DateOnly AsOfDate
);

/// <summary>
/// Result of a payment allocation preview.
/// </summary>
public sealed record AllocationPreviewResultDto(
    decimal InputAmount,
    string CurrencyCode,
    IReadOnlyList<InstallmentAllocationPreviewDto> InstallmentAllocations,
    decimal UnappliedAmount,
    IReadOnlyList<ChargeAllocationPreviewDto> ChargeAllocations,
    bool HasSchedule,
    string? ErrorMessage
);

/// <summary>
/// Preview of allocation to a single installment.
/// </summary>
public sealed record InstallmentAllocationPreviewDto(
    Guid InstallmentKey,
    DateOnly DueDate,
    decimal InstallmentAmount,
    decimal AlreadyPaid,
    decimal OutstandingBefore,
    decimal AllocatedNow,
    decimal OutstandingAfter,
    string Status
);

/// <summary>
/// Preview of allocation to a charge.
/// </summary>
public sealed record ChargeAllocationPreviewDto(
    Guid ChargeId,
    string ChargeType,
    decimal OutstandingBefore,
    decimal AllocatedNow,
    decimal OutstandingAfter
);

/// <summary>
/// Handler to preview payment allocation without recording any events.
/// Uses the SAME allocation logic as RecordPaymentHandler.
/// </summary>
public sealed class PreviewPaymentAllocationHandler
{
    private readonly IEventStore _eventStore;
    private readonly IRuleEngine _ruleEngine;
    private readonly ScheduleExpanderV1 _expander = new();
    private readonly PaymentAllocationService _allocator = new(new OldestDueFirstAllocator());

    public PreviewPaymentAllocationHandler(IEventStore eventStore, IRuleEngine ruleEngine)
    {
        _eventStore = eventStore;
        _ruleEngine = ruleEngine;
    }

    public async Task<AllocationPreviewResultDto> HandleAsync(
        PreviewPaymentAllocationCommand cmd,
        CancellationToken ct = default)
    {
        // Validate input
        if (cmd.Amount <= 0)
        {
            return new AllocationPreviewResultDto(
                InputAmount: cmd.Amount,
                CurrencyCode: cmd.CurrencyCode,
                InstallmentAllocations: Array.Empty<InstallmentAllocationPreviewDto>(),
                UnappliedAmount: 0,
                ChargeAllocations: Array.Empty<ChargeAllocationPreviewDto>(),
                HasSchedule: false,
                ErrorMessage: "Amount must be greater than zero"
            );
        }

        var currency = cmd.CurrencyCode switch
        {
            "EGP" => Currency.EGP,
            "USD" => Currency.USD,
            "EUR" => Currency.EUR,
            _ => new Currency(cmd.CurrencyCode, 2)
        };

        // 1) Load stream up to payment effective date
        var stream = await _eventStore.ReadStreamAsync(
            new StreamId(cmd.ObligationId),
            upTo: cmd.EffectiveDate,
            ct
        );

        if (stream.Count == 0)
        {
            return new AllocationPreviewResultDto(
                InputAmount: cmd.Amount,
                CurrencyCode: cmd.CurrencyCode,
                InstallmentAllocations: Array.Empty<InstallmentAllocationPreviewDto>(),
                UnappliedAmount: 0,
                ChargeAllocations: Array.Empty<ChargeAllocationPreviewDto>(),
                HasSchedule: false,
                ErrorMessage: "Obligation not found"
            );
        }

        // 2) Deserialize events + schedules
        var domainEvents = EventDeserializer.ToDomainEvents(stream).ToList();
        var schedules = EventDeserializer.ToSchedules(stream).ToList();

        // 3) Check for schedule
        if (schedules.Count == 0)
        {
            // No schedule - entire payment goes to unapplied
            return new AllocationPreviewResultDto(
                InputAmount: cmd.Amount,
                CurrencyCode: cmd.CurrencyCode,
                InstallmentAllocations: Array.Empty<InstallmentAllocationPreviewDto>(),
                UnappliedAmount: cmd.Amount,
                ChargeAllocations: Array.Empty<ChargeAllocationPreviewDto>(),
                HasSchedule: false,
                ErrorMessage: null
            );
        }

        // 4) Expand expected installments
        var expected = new List<ExpectedInstallment>();
        foreach (var s in schedules)
        {
            var expanded = await _expander.ExpandAsync(
                s,
                from: new DateOnly(cmd.EffectiveDate.Year - 1, 1, 1),
                to: new DateOnly(cmd.EffectiveDate.Year + 2, 12, 31),
                ct
            );
            expected.AddRange(expanded);
        }

        // 5) Existing allocations (installment allocations)
        var existingAllocations = domainEvents
            .OfType<PaymentAllocated>()
            .ToList();

        // 6) Build projected state for charges
        var projector = new RulesAwareFinancialProjector(_ruleEngine);
        var projectedEvents = new List<ProjectedEvent>();
        foreach (var env in stream)
        {
            var ev = EventDeserializer.ToDomainEvents(new[] { env }).FirstOrDefault();
            if (ev is not null)
                projectedEvents.Add(new ProjectedEvent(env, ev));
        }

        var state = await projector.ReplayAsync(
            projectedEvents,
            expected,
            new ProjectionContext(cmd.EffectiveDate, currency),
            ct
        );

        // 7) Allocate to charges first using AllocationPolicyV1
        var policy = new AllocationPolicyV1();
        var paymentMoney = new Money(cmd.Amount, currency);
        var chargeAlloc = policy.AllocateChargesFirst(state, paymentMoney);

        var chargeAllocations = new List<ChargeAllocationPreviewDto>();
        foreach (var (chargeId, allocAmount) in chargeAlloc.ChargeAllocations)
        {
            var charge = state.Charges.FirstOrDefault(c => c.ChargeId == chargeId);
            if (charge != null)
            {
                // Get paid amount for this charge to compute outstanding
                var chargePaid = state.ChargePayments.TryGetValue(chargeId, out var paid) ? paid.Amount : 0m;
                var chargeOutstanding = charge.Amount.Amount - chargePaid;

                chargeAllocations.Add(new ChargeAllocationPreviewDto(
                    ChargeId: chargeId,
                    ChargeType: charge.Type.ToString(),
                    OutstandingBefore: chargeOutstanding,
                    AllocatedNow: allocAmount.Amount,
                    OutstandingAfter: chargeOutstanding - allocAmount.Amount
                ));
            }
        }

        // 8) Create a dummy payment for installment allocation
        var dummyPaymentEventId = Guid.NewGuid();
        var paymentForInstallments = new PaymentMade(
            cmd.ObligationId,
            chargeAlloc.RemainingForInstallments,
            cmd.EffectiveDate,
            null
        );

        // 9) Allocate remaining to installments (using same allocator as RecordPaymentHandler)
        var allocations = _allocator.AllocatePayment(
            dummyPaymentEventId,
            paymentForInstallments,
            expected,
            existingAllocations,
            cmd.EffectiveDate
        );

        // 10) Build installment preview list
        var installmentPreviews = new List<InstallmentAllocationPreviewDto>();

        // Build a lookup of how much was already paid to each installment (using InstallmentKey.Value as Guid key)
        var paidByInstallment = existingAllocations
            .GroupBy(a => a.InstallmentKey)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Amount.Amount));

        // Build a lookup of how much this payment allocates to each installment
        var newAllocationByInstallment = allocations
            .GroupBy(a => a.InstallmentKey)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Amount.Amount));

        foreach (var inst in expected.OrderBy(i => i.DueDate))
        {
            var instKeyGuid = inst.InstallmentKey.Value;
            var alreadyPaid = paidByInstallment.TryGetValue(instKeyGuid, out var paid) ? paid : 0m;
            var outstandingBefore = inst.Amount.Amount - alreadyPaid;
            var allocatedNow = newAllocationByInstallment.TryGetValue(instKeyGuid, out var alloc) ? alloc : 0m;
            var outstandingAfter = outstandingBefore - allocatedNow;

            // Determine status
            string status;
            if (outstandingAfter <= 0)
                status = "Paid";
            else if (allocatedNow > 0)
                status = "Partial";
            else if (inst.DueDate < cmd.AsOfDate)
                status = "Overdue";
            else if (inst.DueDate == cmd.AsOfDate)
                status = "Due Today";
            else
                status = "Upcoming";

            // Only include installments that have outstanding balance OR will receive allocation
            if (outstandingBefore > 0 || allocatedNow > 0)
            {
                installmentPreviews.Add(new InstallmentAllocationPreviewDto(
                    InstallmentKey: instKeyGuid,
                    DueDate: inst.DueDate,
                    InstallmentAmount: inst.Amount.Amount,
                    AlreadyPaid: alreadyPaid,
                    OutstandingBefore: Math.Max(0, outstandingBefore),
                    AllocatedNow: allocatedNow,
                    OutstandingAfter: Math.Max(0, outstandingAfter),
                    Status: status
                ));
            }
        }

        // 11) Calculate unapplied amount
        var allocatedToInstallments = allocations.Sum(a => a.Amount.Amount);
        var unappliedAmount = chargeAlloc.RemainingForInstallments.Amount - allocatedToInstallments;

        return new AllocationPreviewResultDto(
            InputAmount: cmd.Amount,
            CurrencyCode: cmd.CurrencyCode,
            InstallmentAllocations: installmentPreviews,
            UnappliedAmount: Math.Max(0, unappliedAmount),
            ChargeAllocations: chargeAllocations,
            HasSchedule: true,
            ErrorMessage: null
        );
    }
}
