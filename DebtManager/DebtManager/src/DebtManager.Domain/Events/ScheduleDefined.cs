namespace DebtManager.Domain.Events;

public sealed record ScheduleDefined(
    Guid ScheduleId,
    Guid ObligationId,
    string ScheduleType,
    string ScheduleSpecJson,
    string Timezone,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
