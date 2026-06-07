using System.Text.Json;
using DebtManager.Domain.Allocation;
using DebtManager.Domain.Events;
using DebtManager.Domain.Installments;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Scheduling;
using DebtManager.Domain.Services;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Tests;

public class EndToEndInstallmentProjectionTests
{
    [Fact]
    public async Task EndToEnd_TuitionSchedule_PaymentAllocatesAndProjectsOutstanding()
    {
        var egp = Currency.EGP;
        var obligationId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();

        // 1) Create tuition schedule
        var spec = new FixedDatesScheduleSpec(
            "EGP",
            new[]
            {
                new FixedDateItem(new DateOnly(2026, 9, 15), 10000),
                new FixedDateItem(new DateOnly(2026, 11, 30), 10000),
                new FixedDateItem(new DateOnly(2027, 2, 28), 10000),
            },
            new[] { "tuition" }
        );

        var schedule = new ScheduleDefinition(
            scheduleId,
            obligationId,
            "fixed_dates",
            JsonSerializer.Serialize(spec, DebtManager.Domain.ValueObjects.DomainJson.Options),
            "Africa/Cairo"
        );

        // 2) Expand expected installments
        var expander = new ScheduleExpanderV1();
        var expected = await expander.ExpandAsync(
            schedule,
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 12, 31),
            CancellationToken.None);

        // 3) Make a payment of 12k
        var payment = new PaymentMade(
            obligationId,
            new Money(12000, egp),
            new DateOnly(2026, 9, 20),
            "Pay 12k");

        var paymentEventId = Guid.NewGuid();

        // 4) Allocate payment -> PaymentAllocated events
        var allocator = new OldestDueFirstAllocator();
        var service = new PaymentAllocationService(allocator);

        var allocEvents = service.AllocatePayment(
            paymentEventId,
            payment,
            expected,
            Array.Empty<PaymentAllocated>(),
            new DateOnly(2026, 9, 20));

        // 5) Replay
        var events = new IDomainEvent[]
        {
            new ObligationCreated(obligationId, "Tuition", "Education", new Money(30000, egp), new DateOnly(2026, 9, 1), "EGP"),
            payment,
        }.Concat(allocEvents).ToList();

        var projector = new FinancialProjector();
        var state = projector.Replay(
            events,
            expected,
            new ProjectionContext(new DateOnly(2026, 12, 31), egp));

        // 6) Validate installment states:
        // First installment should be fully paid (10k)
        // Second installment should have 2k paid, 8k outstanding
        var i1 = state.Installments.OrderBy(x => x.DueDate).First();
        var i2 = state.Installments.OrderBy(x => x.DueDate).Skip(1).First();

        Assert.True(i1.IsFullyPaid);
        Assert.Equal(10000, i1.Paid.Amount);

        Assert.False(i2.IsFullyPaid);
        Assert.Equal(2000, i2.Paid.Amount);
        Assert.Equal(8000, i2.Outstanding.Amount);
    }
}
