using DebtManager.Domain.Installments;

namespace DebtManager.Domain.Scheduling;

public sealed record ScheduleDefinition(
    Guid ScheduleId,
    Guid ObligationId,
    string ScheduleType,
    string ScheduleSpecJson,
    string Timezone
);

public interface IScheduleExpander
{
    Task<IReadOnlyList<ExpectedInstallment>> ExpandAsync(
        ScheduleDefinition schedule,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);
}
