using System.Text.Json;
using DebtManager.Domain.Scheduling;

namespace DebtManager.Domain.Tests;

public class ScheduleExpanderTests
{
    [Fact]
    public async Task FixedDatesSchedule_ExpandsTuitionInstallments()
    {
        var obligationId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();

        var spec = new FixedDatesScheduleSpec(
            "EGP",
            new[]
            {
                new FixedDateItem(new DateOnly(2026, 9, 15), 10000),
                new FixedDateItem(new DateOnly(2026, 11, 30), 10000),
                new FixedDateItem(new DateOnly(2027, 2, 28), 10000),
            },
            new[] { "tuition", "education" }
        );

        var schedule = new ScheduleDefinition(
            scheduleId,
            obligationId,
            "fixed_dates",
            JsonSerializer.Serialize(spec, DebtManager.Domain.ValueObjects.DomainJson.Options),
            "Africa/Cairo"
        );

        var expander = new ScheduleExpanderV1();

        var installments = await expander.ExpandAsync(
            schedule,
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 12, 31),
            CancellationToken.None
        );

        Assert.Equal(3, installments.Count);
        Assert.Equal(new DateOnly(2026, 9, 15), installments[0].DueDate);
        Assert.Equal(new DateOnly(2026, 11, 30), installments[1].DueDate);
        Assert.Equal(new DateOnly(2027, 2, 28), installments[2].DueDate);
        Assert.Equal("fixed_dates", schedule.ScheduleType);
    }
}
