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
        return schedule.ScheduleType switch
        {
            "fixed_dates" => ExpandFixedDates(schedule, from, to),
            "amortization" => ExpandAmortization(schedule, from, to),
            var t when t.StartsWith("recurring_") => ExpandRecurring(schedule, from, to),
            "MonthlyFixed" => ExpandMonthlyFixed(schedule, from, to),
            _ => throw new NotSupportedException($"ScheduleType '{schedule.ScheduleType}' not supported in v1.")
        };
    }

    private static Task<IReadOnlyList<ExpectedInstallment>> ExpandFixedDates(
        ScheduleDefinition schedule, DateOnly from, DateOnly to)
    {
        var spec = JsonSerializer.Deserialize<FixedDatesScheduleSpec>(
                       schedule.ScheduleSpecJson, DomainJson.Options)
                   ?? throw new InvalidOperationException("Invalid schedule_spec_json for fixed_dates.");

        var currency = ResolveCurrency(spec.CurrencyCode);

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

    private static Task<IReadOnlyList<ExpectedInstallment>> ExpandRecurring(
        ScheduleDefinition schedule, DateOnly from, DateOnly to)
    {
        using var doc = JsonDocument.Parse(schedule.ScheduleSpecJson);
        var root = doc.RootElement;

        var patternStr = root.TryGetProperty("pattern", out var patternEl)
            ? patternEl.GetString() ?? "monthly"
            : "monthly";

        var pattern = patternStr.ToLowerInvariant() switch
        {
            "monthly" => RecurrencePattern.Monthly,
            "quarterly" => RecurrencePattern.Quarterly,
            "annual" => RecurrencePattern.Annual,
            "semiannual" => RecurrencePattern.SemiAnnual,
            "weekly" => RecurrencePattern.Weekly,
            "biweekly" => RecurrencePattern.Biweekly,
            _ => RecurrencePattern.Monthly
        };

        var dayOfMonth = root.TryGetProperty("dayOfMonth", out var dayEl) ? dayEl.GetInt32() : 1;
        var startDate = root.TryGetProperty("startDate", out var startEl)
            ? DateOnly.Parse(startEl.GetString()!)
            : from;
        DateOnly? endDate = root.TryGetProperty("endDate", out var endEl) && endEl.ValueKind != JsonValueKind.Null
            ? DateOnly.Parse(endEl.GetString()!)
            : null;
        int? maxOccurrences = root.TryGetProperty("maxOccurrences", out var maxEl) && maxEl.ValueKind != JsonValueKind.Null
            ? maxEl.GetInt32()
            : null;
        var amount = root.TryGetProperty("amount", out var amtEl) ? amtEl.GetDecimal() : 0m;
        var currencyCode = root.TryGetProperty("currencyCode", out var curEl) ? curEl.GetString() ?? "EGP" : "EGP";
        var currency = ResolveCurrency(currencyCode);

        var spec = new RecurringScheduleSpec(
            scheduleId: schedule.ScheduleId,
            obligationId: schedule.ObligationId,
            pattern: pattern,
            dayOfMonth: Math.Clamp(dayOfMonth, 1, 31),
            startDate: startDate,
            endDate: endDate,
            maxOccurrences: maxOccurrences,
            installmentAmount: new Money(amount, currency)
        );

        var installments = spec.Expand(from, to)
            .Select(i => new ExpectedInstallment(
                new InstallmentKey(DeterministicInstallmentGuid(schedule.ScheduleId, i.DueDate)),
                schedule.ObligationId,
                i.DueDate,
                i.ExpectedAmount,
                schedule.ScheduleId,
                i.Tags
            ))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<ExpectedInstallment>>(installments);
    }

    private static Task<IReadOnlyList<ExpectedInstallment>> ExpandAmortization(
        ScheduleDefinition schedule, DateOnly from, DateOnly to)
    {
        using var doc = JsonDocument.Parse(schedule.ScheduleSpecJson);
        var root = doc.RootElement;

        var principal = root.TryGetProperty("principal", out var pEl) ? pEl.GetDecimal() : 0m;
        var annualRate = root.TryGetProperty("annualInterestRate", out var rEl) ? rEl.GetDecimal() : 0m;
        var termMonths = root.TryGetProperty("termMonths", out var tEl) ? tEl.GetInt32() : 12;
        var startDate = root.TryGetProperty("startDate", out var sEl)
            ? DateOnly.Parse(sEl.GetString()!)
            : from;
        var currencyCode = root.TryGetProperty("currencyCode", out var curEl) ? curEl.GetString() ?? "EGP" : "EGP";
        var currency = ResolveCurrency(currencyCode);

        var spec = new AmortizationScheduleSpec(
            scheduleId: schedule.ScheduleId,
            obligationId: schedule.ObligationId,
            principal: new Money(principal, currency),
            annualInterestRate: annualRate,
            termInMonths: Math.Max(1, termMonths),
            firstPaymentDate: startDate,
            dayOfMonth: Math.Clamp(startDate.Day, 1, 31)
        );

        var installments = spec.Expand(from, to)
            .Select(i => new ExpectedInstallment(
                new InstallmentKey(DeterministicInstallmentGuid(schedule.ScheduleId, i.DueDate)),
                schedule.ObligationId,
                i.DueDate,
                i.ExpectedAmount,
                schedule.ScheduleId,
                i.Tags
            ))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<ExpectedInstallment>>(installments);
    }

    /// <summary>
    /// Handles legacy "MonthlyFixed" schedule type emitted by demo seeding.
    /// Parses the same JSON shape as recurring specs.
    /// </summary>
    private static Task<IReadOnlyList<ExpectedInstallment>> ExpandMonthlyFixed(
        ScheduleDefinition schedule, DateOnly from, DateOnly to)
    {
        using var doc = JsonDocument.Parse(schedule.ScheduleSpecJson);
        var root = doc.RootElement;

        var dayOfMonth = root.TryGetProperty("dayOfMonth", out var dayEl) ? dayEl.GetInt32() : 1;
        var startDate = root.TryGetProperty("startDate", out var startEl)
            ? DateOnly.Parse(startEl.GetString()!)
            : from;
        DateOnly? endDate = root.TryGetProperty("endDate", out var endEl) && endEl.ValueKind != JsonValueKind.Null
            ? DateOnly.Parse(endEl.GetString()!)
            : null;
        var amount = root.TryGetProperty("amount", out var amtEl) ? amtEl.GetDecimal() : 0m;
        var currencyCode = root.TryGetProperty("currency", out var curEl) ? curEl.GetString() ?? "EGP" : "EGP";
        var currency = ResolveCurrency(currencyCode);

        var spec = new RecurringScheduleSpec(
            scheduleId: schedule.ScheduleId,
            obligationId: schedule.ObligationId,
            pattern: RecurrencePattern.Monthly,
            dayOfMonth: Math.Clamp(dayOfMonth, 1, 31),
            startDate: startDate,
            endDate: endDate,
            maxOccurrences: null,
            installmentAmount: new Money(amount, currency)
        );

        var installments = spec.Expand(from, to)
            .Select(i => new ExpectedInstallment(
                new InstallmentKey(DeterministicInstallmentGuid(schedule.ScheduleId, i.DueDate)),
                schedule.ObligationId,
                i.DueDate,
                i.ExpectedAmount,
                schedule.ScheduleId,
                i.Tags
            ))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<ExpectedInstallment>>(installments);
    }

    private static Currency ResolveCurrency(string code) => code switch
    {
        "EGP" => Currency.EGP,
        "USD" => Currency.USD,
        "EUR" => Currency.EUR,
        _ => new Currency(code, 2)
    };

    private static Guid DeterministicInstallmentGuid(Guid scheduleId, DateOnly dueDate)
    {
        var input = $"{scheduleId:N}|{dueDate:yyyy-MM-dd}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }
}
