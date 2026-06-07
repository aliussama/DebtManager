using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Scheduling;
using DebtManager.Domain.ValueObjects;
using DebtManager.Domain.Installments;

namespace DebtManager.Domain.Tests;

public class RulesAwareProjectionTests
{
    [Fact]
    public async Task OverdueInstallment_ProducesPenaltyCharge()
    {
        var egp = Currency.EGP;
        var obligationId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();

        // One installment due on 2026-09-15
        var spec = new FixedDatesScheduleSpec(
            "EGP",
            new[] { new FixedDateItem(new DateOnly(2026, 9, 15), 10000) },
            new[] { "tuition" });

        var schedule = new ScheduleDefinition(
            scheduleId, obligationId, "fixed_dates", JsonSerializer.Serialize(spec, DebtManager.Domain.ValueObjects.DomainJson.Options), "Africa/Cairo");

        var expected = await new ScheduleExpanderV1().ExpandAsync(
            schedule,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            CancellationToken.None);

        // No payment -> overdue by 5 days at 2026-09-20
        var events = new IDomainEvent[]
        {
            new ObligationCreated(obligationId, "Tuition", "Education", new Money(10000, egp), new DateOnly(2026, 9, 1), "EGP"),
        };

        var projector = new RulesAwareFinancialProjector(new FakeRuleEngine());

        var state = await projector.ReplayAsync(
            events,
            expected,
            new ProjectionContext(new DateOnly(2026, 9, 20), egp),
            CancellationToken.None);

        Assert.Single(state.Charges);
        Assert.Equal(100m, state.Charges[0].Amount.Amount);
        Assert.Equal("Late Penalty", state.Charges[0].Label);
    }
}
