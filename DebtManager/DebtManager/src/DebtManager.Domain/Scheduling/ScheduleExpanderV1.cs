using System.Text.Json;
using DebtManager.Domain.Installments;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Scheduling;

public sealed class ScheduleExpanderV1 : IScheduleExpander
{
    public Task<IReadOnlyList<ExpectedInstallment>> ExpandAsync(
        ScheduleDefinition schedule,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        if (schedule.ScheduleType != "fixed_dates")
            throw new NotSupportedException($"ScheduleType '{schedule.ScheduleType}' not supported in v1.");

        var spec = JsonSerializer.Deserialize<FixedDatesScheduleSpec>(schedule.ScheduleSpecJson, DebtManager.Domain.ValueObjects.DomainJson.Options)
                   ?? throw new InvalidOperationException("Invalid schedule_spec_json for fixed_dates.");

        // Currency registry is a full feature later; for now support common ones.
        var currency = spec.CurrencyCode switch
        {
            "EGP" => Currency.EGP,
            "USD" => Currency.USD,
            "EUR" => Currency.EUR,
            _ => new Currency(spec.CurrencyCode, 2)
        };

        var result = spec.Dates
            .Where(d => d.DueDate >= from && d.DueDate <= to)
            .OrderBy(d => d.DueDate)
.Select(d => new ExpectedInstallment(
    new InstallmentKey(DeterministicInstallmentGuid(schedule.ScheduleId, d.DueDate)),
    schedule.ObligationId,
    d.DueDate,
    new Money(d.Amount, currency),
    schedule.ScheduleId,
    spec.Tags
))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<ExpectedInstallment>>(result);
    }
    private static Guid DeterministicInstallmentGuid(Guid scheduleId, DateOnly dueDate)
    {
        // Stable across runs/machines. No randomness.
        // Key = SHA256("scheduleId|yyyy-MM-dd"), take first 16 bytes as GUID.
        var input = $"{scheduleId:N}|{dueDate:yyyy-MM-dd}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);

        var hash = System.Security.Cryptography.SHA256.HashData(bytes);

        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);

        return new Guid(guidBytes);
    }

}
