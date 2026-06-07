using DebtManager.Application.Internal;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

public sealed record TimelineItem(
    DateOnly Date,
    string Type,
    Money Amount,
    string Description,
    Money RunningBalance
);
public sealed record CrisisWindow(
    DateOnly Start,
    DateOnly End,
    Money LowestBalance,
    IReadOnlyList<CrisisContributor> TopContributors
);

public sealed record CrisisContributor(
    DateOnly Date,
    string Type,
    Money Amount,
    string Description
);
public sealed record GetPortfolioTimelineResult(
    IReadOnlyList<TimelineItem> Items
);
public sealed class GetPortfolioTimelineHandler
{
    private readonly IEventStore _store;
    private readonly GetFinancialSnapshotHandler _snapshots;

    public GetPortfolioTimelineHandler(IEventStore store, GetFinancialSnapshotHandler snapshots)
    {
        _store = store;
        _snapshots = snapshots;
    }

    public async Task<GetPortfolioTimelineResult> HandleAsync(
        DateOnly asOfDate,
        CancellationToken ct)
    {
        // Step 1: read all events up to asOfDate
        // Read everything since a very early safe date (SQLite + ISO parsing safe)
        var envelopes = await _store.ReadAllAsync(
            since: new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ct
        );
        if (envelopes is null)
            envelopes = Array.Empty<EventEnvelope>();

        // Step 2: deserialize domain events
        var events = EventDeserializer.ToDomainEvents(envelopes);

        // Step 3: build timeline
        var items = new List<TimelineItem>();
        var balance = Money.Zero(Currency.EGP);

        foreach (var ev in events
            .Where(e => e.EffectiveDate <= asOfDate)
            .OrderBy(e => e.EffectiveDate))
        {
            switch (ev)
            {
                case IncomeRecorded inc:
                    {
                        if (inc.Amount.Currency.Code != balance.Currency.Code)
                            throw new InvalidOperationException("Portfolio timeline v1 requires single currency. Convert or store FX rates later.");
                        balance = balance.Add(inc.Amount);
                        items.Add(new TimelineItem(
                            Date: inc.EffectiveDate,
                            Type: "Income",
                            Amount: inc.Amount,
                            Description: inc.Source,
                            RunningBalance: balance
                        ));
                        break;
                    }

                case ExpenseRecorded exp:
                    {
                        // show negative delta
                        if (exp.Amount.Currency.Code != balance.Currency.Code)
                            throw new InvalidOperationException("Portfolio timeline v1 requires single currency. Convert or store FX rates later.");
                        var delta = Money.Zero(exp.Amount.Currency).Subtract(exp.Amount);
                        balance = balance.Add(delta);

                        items.Add(new TimelineItem(
                            Date: exp.EffectiveDate,
                            Type: "Expense",
                            Amount: delta,
                            Description: $"{exp.Category} - {exp.Notes}",
                            RunningBalance: balance
                        ));
                        break;
                    }

                case PaymentMade pm:
                    {
                        // Payment decreases cash balance
                        if (pm.Amount.Currency.Code != balance.Currency.Code)
                            throw new InvalidOperationException("Portfolio timeline v1 requires single currency. Convert or store FX rates later.");
                        var delta = Money.Zero(pm.Amount.Currency).Subtract(pm.Amount);
                        balance = balance.Add(delta);

                        items.Add(new TimelineItem(
                            Date: pm.EffectiveDate,
                            Type: "Payment",
                            Amount: delta,
                            Description: $"Payment: {pm.Reference ?? "n/a"} (Obligation {pm.ObligationId})",
                            RunningBalance: balance
                        ));
                        break;
                    }
            }
        }

        // --- CHARGES (derived via rules-aware snapshots) ---
        var obligationIds = events
            .OfType<ObligationCreated>()
            .Select(o => o.ObligationId)
            .Distinct()
            .ToList();

        foreach (var oid in obligationIds)
        {
            var snap = await _snapshots.HandleAsync(oid, asOfDate, ct);

            foreach (var c in snap.Charges)
            {
                var delta = Money.Zero(c.Amount.Currency).Subtract(c.Amount);
                balance = balance.Add(delta);

                items.Add(new TimelineItem(
                    Date: c.EffectiveDate,
                    Type: "Charge",
                    Amount: delta,
                    Description: $"Charge: {c.Label} (Obligation {oid})",
                    RunningBalance: balance
                ));
            }
            if (snap.Charges is null) continue;
        }

        var ordered = items
            .OrderBy(i => i.Date)
            .ToList();

        var rb = Money.Zero(Currency.EGP);
        var rebuilt = new List<TimelineItem>();

        foreach (var it in ordered)
        {
            // currency guard (v1 single currency)
            if (it.Amount.Currency.Code != rb.Currency.Code)
                throw new InvalidOperationException("Portfolio timeline v1 requires single currency.");

            rb = rb.Add(it.Amount);

            rebuilt.Add(it with { RunningBalance = rb });
        }

        return new GetPortfolioTimelineResult(rebuilt);
    }
    public static IReadOnlyList<CrisisWindow> DetectCrisisWindows(IReadOnlyList<TimelineItem> items)
    {
        var crises = new List<CrisisWindow>();

        CrisisWindow? current = null;
        Money lowest = Money.Zero(Currency.EGP);

        foreach (var it in items)
        {
            if (it.RunningBalance.Amount < 0m)
            {
                if (current is null)
                {
                    lowest = it.RunningBalance;
                    current = new CrisisWindow(
                        Start: it.Date,
                        End: it.Date,
                        LowestBalance: it.RunningBalance,
                        TopContributors: new List<CrisisContributor>()
                    );
                }
                else
                {
                    if (it.RunningBalance.Amount < lowest.Amount)
                        lowest = it.RunningBalance;

                    current = current with { End = it.Date, LowestBalance = lowest };
                }
            }
            else
            {
                if (current is not null)
                {
                    crises.Add(current);
                    current = null;
                }
            }
        }

        if (current is not null)
            crises.Add(current);

        return crises.AsReadOnly();
    }
}
