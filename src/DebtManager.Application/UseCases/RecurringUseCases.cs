using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Commands ---

public sealed record CreateRecurringCommand(
    Guid? RecurringId, string Kind, Guid AccountId,
    decimal Amount, string CurrencyCode, Guid? CategoryId,
    string? Notes, string? Reference, string Frequency, int Interval,
    DateOnly StartDate, DateOnly? EndDate, bool AutoPostEnabled);

public sealed record ArchiveRecurringCommand(Guid RecurringId, string Reason);

public sealed record PostRecurringNowCommand(Guid RecurringId, DateOnly PostDate);

// --- DTOs ---

public sealed record RecurringDashboardItemDto(
    Guid RecurringId, string Kind, decimal Amount, string CurrencyCode,
    string? Reference, string Frequency, int Interval,
    DateOnly StartDate, DateOnly? EndDate,
    DateOnly? NextDueDate, string Status, // "Due", "Overdue", "Upcoming", "Ended"
    bool IsArchived);

public sealed record RecurringDashboardDto(IReadOnlyList<RecurringDashboardItemDto> Items);

// --- Handlers ---

public sealed class CreateRecurringHandler
{
    private readonly IEventStore _store;
    public CreateRecurringHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(CreateRecurringCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var id = cmd.RecurringId ?? Guid.NewGuid();
        var ev = new RecurringTransactionCreated(
            id, cmd.Kind, cmd.AccountId, cmd.Amount, cmd.CurrencyCode,
            cmd.CategoryId, cmd.Notes, cmd.Reference, cmd.Frequency, cmd.Interval,
            cmd.StartDate, cmd.EndDate, cmd.AutoPostEnabled, cmd.StartDate);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(id),
            nameof(RecurringTransactionCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return id;
    }
}

public sealed class ArchiveRecurringHandler
{
    private readonly IEventStore _store;
    public ArchiveRecurringHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveRecurringCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new RecurringTransactionArchived(cmd.RecurringId, DateOnly.FromDateTime(DateTime.Today), cmd.Reason);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.RecurringId),
            nameof(RecurringTransactionArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class PostRecurringNowHandler
{
    private readonly IEventStore _store;
    public PostRecurringNowHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(PostRecurringNowCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = RecurringProjector.Project(envelopes);

        if (!state.Items.TryGetValue(cmd.RecurringId, out var item))
            throw new InvalidOperationException("Recurring transaction not found");

        if (item.IsArchived)
            throw new InvalidOperationException("Cannot post an archived recurring transaction");

        if (item.EndDate.HasValue && cmd.PostDate > item.EndDate.Value)
            throw new InvalidOperationException("Post date is after the recurring end date");

        // Idempotency: find the scheduled cycle date that this post matches
        var scheduledDate = RecurringProjector.GetScheduledDateForCycle(item, cmd.PostDate);
        if (scheduledDate.HasValue && item.PostedDates.Contains(scheduledDate.Value))
            throw new InvalidOperationException($"Already posted for cycle date {scheduledDate.Value}");

        var correlationId = Guid.NewGuid();
        var postedEventId = Guid.NewGuid();

        // Create the actual income or expense event
        var currency = item.CurrencyCode switch
        {
            "EGP" => Currency.EGP,
            "USD" => Currency.USD,
            "EUR" => Currency.EUR,
            _ => new Currency(item.CurrencyCode, 2)
        };

        if (item.Kind == "income")
        {
            var incomeEv = new IncomeRecorded(
                item.AccountId, new Money(item.Amount, currency),
                cmd.PostDate, item.Reference ?? "Recurring income");
            var incomeEnv = new EventEnvelope(
                new EventId(postedEventId), new StreamId(item.AccountId),
                nameof(IncomeRecorded), DateTimeOffset.UtcNow, cmd.PostDate,
                actorUserId, deviceId, correlationId, null, 1,
                JsonSerializer.Serialize(incomeEv, DomainJson.Options));
            await _store.AppendAsync(incomeEnv, ct);
        }
        else
        {
            var expenseEv = new ExpenseRecorded(
                item.AccountId, new Money(item.Amount, currency),
                cmd.PostDate, item.Reference ?? "Recurring expense", item.Notes ?? string.Empty);
            var expenseEnv = new EventEnvelope(
                new EventId(postedEventId), new StreamId(item.AccountId),
                nameof(ExpenseRecorded), DateTimeOffset.UtcNow, cmd.PostDate,
                actorUserId, deviceId, correlationId, null, 1,
                JsonSerializer.Serialize(expenseEv, DomainJson.Options));
            await _store.AppendAsync(expenseEnv, ct);
        }

        // Append the posted-link event
        var postedEv = new RecurringTransactionPosted(cmd.RecurringId, postedEventId, cmd.PostDate);
        var postedEnv = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.RecurringId),
            nameof(RecurringTransactionPosted), DateTimeOffset.UtcNow, cmd.PostDate,
            actorUserId, deviceId, correlationId, new EventId(postedEventId).Value, 1,
            JsonSerializer.Serialize(postedEv, DomainJson.Options));
        await _store.AppendAsync(postedEnv, ct);

        return postedEventId;
    }
}

public sealed class GetRecurringDashboardHandler
{
    private readonly IEventStore _store;
    public GetRecurringDashboardHandler(IEventStore store) => _store = store;

    public async Task<RecurringDashboardDto> HandleAsync(DateOnly asOfDate, Guid? accountId, CancellationToken ct)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = RecurringProjector.Project(envelopes);

        var items = state.Items.Values.AsEnumerable();
        if (accountId.HasValue)
            items = items.Where(i => i.AccountId == accountId.Value);

        var dtos = items.Select(item =>
        {
            var nextDue = RecurringProjector.ComputeNextDueDate(item, asOfDate);
            string status;
            if (item.IsArchived) status = "Ended";
            else if (item.EndDate.HasValue && asOfDate > item.EndDate.Value) status = "Ended";
            else if (nextDue.HasValue && nextDue.Value < asOfDate) status = "Overdue";
            else if (nextDue.HasValue && nextDue.Value == asOfDate) status = "Due";
            else status = "Upcoming";

            return new RecurringDashboardItemDto(
                item.RecurringId, item.Kind, item.Amount, item.CurrencyCode,
                item.Reference, item.Frequency, item.Interval,
                item.StartDate, item.EndDate, nextDue, status, item.IsArchived);
        })
        .OrderBy(d => d.IsArchived)
        .ThenBy(d => d.NextDueDate ?? DateOnly.MaxValue)
        .ToList();

        return new RecurringDashboardDto(dtos);
    }
}
