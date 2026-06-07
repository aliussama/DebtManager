using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Scheduling;

// JSON spec example:
// { "currency":"EGP", "dates":[ {"due":"2026-09-15","amount":10000}, ... ], "tags":["tuition"] }
public sealed record FixedDatesScheduleSpec(
    string CurrencyCode,
    IReadOnlyList<FixedDateItem> Dates,
    IReadOnlyList<string> Tags
);

public sealed record FixedDateItem(DateOnly DueDate, decimal Amount);
