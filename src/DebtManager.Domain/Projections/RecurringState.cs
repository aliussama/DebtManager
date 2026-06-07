using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

/// <summary>
/// State of a single recurring transaction template.
/// </summary>
public sealed class RecurringItem
{
    public Guid RecurringId { get; set; }
    public string Kind { get; set; } = string.Empty; // "income" or "expense"
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public string? Notes { get; set; }
    public string? Reference { get; set; }
    public string Frequency { get; set; } = string.Empty; // Weekly, Monthly, Quarterly, Yearly
    public int Interval { get; set; } = 1;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool AutoPostEnabled { get; set; }
    public bool IsArchived { get; set; }
    public List<DateOnly> PostedDates { get; } = new();
}

/// <summary>
/// Full recurring transaction state derived from events.
/// </summary>
public sealed class RecurringState
{
    public Dictionary<Guid, RecurringItem> Items { get; } = new();
}

/// <summary>
/// Projects recurring transaction events.
/// </summary>
public static class RecurringProjector
{
    public static RecurringState Project(IEnumerable<EventEnvelope> envelopes)
    {
        var state = new RecurringState();
        var opt = DomainJson.Options;

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value);

        foreach (var env in ordered)
        {
            switch (env.EventType)
            {
                case nameof(RecurringTransactionCreated):
                {
                    var ev = JsonSerializer.Deserialize<RecurringTransactionCreated>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Items[ev.RecurringId] = new RecurringItem
                    {
                        RecurringId = ev.RecurringId,
                        Kind = ev.Kind,
                        AccountId = ev.AccountId,
                        Amount = ev.Amount,
                        CurrencyCode = ev.CurrencyCode,
                        CategoryId = ev.CategoryId,
                        Notes = ev.Notes,
                        Reference = ev.Reference,
                        Frequency = ev.Frequency,
                        Interval = ev.Interval,
                        StartDate = ev.StartDate,
                        EndDate = ev.EndDate,
                        AutoPostEnabled = ev.AutoPostEnabled
                    };
                    break;
                }
                case nameof(RecurringTransactionArchived):
                {
                    var ev = JsonSerializer.Deserialize<RecurringTransactionArchived>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Items.TryGetValue(ev.RecurringId, out var item))
                        item.IsArchived = true;
                    break;
                }
                case nameof(RecurringTransactionPosted):
                {
                    var ev = JsonSerializer.Deserialize<RecurringTransactionPosted>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Items.TryGetValue(ev.RecurringId, out var item))
                        item.PostedDates.Add(ev.EffectiveDate);
                    break;
                }
            }
        }

        return state;
    }

    /// <summary>
    /// Computes the next due date for a recurring item as-of a given date.
    /// Returns null if ended or fully posted.
    /// </summary>
    public static DateOnly? ComputeNextDueDate(RecurringItem item, DateOnly asOf)
    {
        if (item.IsArchived) return null;
        if (item.EndDate.HasValue && asOf > item.EndDate.Value) return null;

        var candidate = item.StartDate;
        while (candidate <= asOf || item.PostedDates.Contains(candidate))
        {
            if (!item.PostedDates.Contains(candidate) && candidate <= asOf)
                return candidate; // overdue

            candidate = AdvanceDate(candidate, item.Frequency, item.Interval);
            if (item.EndDate.HasValue && candidate > item.EndDate.Value)
                return null;
        }

        return candidate;
    }

    /// <summary>
    /// Computes the scheduled date for a given cycle that matches a post date.
    /// Used for idempotency check.
    /// </summary>
    public static DateOnly? GetScheduledDateForCycle(RecurringItem item, DateOnly postDate)
    {
        var candidate = item.StartDate;
        while (candidate <= postDate)
        {
            if (candidate == postDate) return candidate;
            var next = AdvanceDate(candidate, item.Frequency, item.Interval);
            if (next > postDate) return candidate; // closest prior scheduled date
            candidate = next;
        }
        return candidate == postDate ? candidate : null;
    }

    private static DateOnly AdvanceDate(DateOnly date, string frequency, int interval) =>
        frequency switch
        {
            "Weekly" => date.AddDays(7 * interval),
            "Monthly" => date.AddMonths(interval),
            "Quarterly" => date.AddMonths(3 * interval),
            "Yearly" => date.AddYears(interval),
            _ => date.AddMonths(interval)
        };
}
