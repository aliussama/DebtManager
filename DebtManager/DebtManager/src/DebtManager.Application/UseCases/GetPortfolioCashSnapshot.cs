using DebtManager.Domain.Cash;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;
using DebtManager.Application.Internal;

namespace DebtManager.Application.UseCases;

public sealed record PortfolioCashSnapshot(
    DateOnly AsOfDate,
    Money Balance,
    IReadOnlyList<(DateOnly Date, Money Delta, string Label)> Movements
);

public sealed class GetPortfolioCashSnapshotHandler
{
    private readonly IEventStore _store;

    public GetPortfolioCashSnapshotHandler(IEventStore store) => _store = store;

    public async Task<PortfolioCashSnapshot> HandleAsync(DateOnly asOfDate, CancellationToken ct)
    {
        var stream = await _store.ReadStreamAsync(new StreamId(DefaultAccount.AccountId), upTo: asOfDate, ct);

        // reuse EventDeserializer by adding cases for IncomeRecorded/ExpenseRecorded later
        var events = EventDeserializer.ToDomainEvents(stream).ToList();

        var baseCurrency = Currency.EGP;
        var bal = Money.Zero(baseCurrency);

        var movements = new List<(DateOnly, Money, string)>();

        foreach (var ev in events.Where(e => e.EffectiveDate <= asOfDate).OrderBy(e => e.EffectiveDate))
        {
            switch (ev)
            {
                case IncomeRecorded inc:
                    bal = bal.Add(inc.Amount);
                    movements.Add((inc.EffectiveDate, inc.Amount, $"Income: {inc.Source}"));
                    break;

                case ExpenseRecorded exp:
                    bal = bal.Subtract(exp.Amount);
                    var neg = Money.Zero(exp.Amount.Currency).Subtract(exp.Amount);
                    movements.Add((exp.EffectiveDate, neg, $"Expense: {exp.Category} {exp.Notes}"));
                    break;
            }
        }

        return new PortfolioCashSnapshot(asOfDate, bal, movements);
    }
}
