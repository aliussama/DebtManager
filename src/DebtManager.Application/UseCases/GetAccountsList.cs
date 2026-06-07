using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;

namespace DebtManager.Application.UseCases;

public sealed record AccountListItemDto(
    Guid AccountId,
    string Name,
    string AccountType,
    string CurrencyCode,
    bool IsArchived,
    decimal Balance
);

public sealed class GetAccountsListHandler
{
    private readonly IEventStore _store;

    public GetAccountsListHandler(IEventStore store) => _store = store;

    public async Task<IReadOnlyList<AccountListItemDto>> HandleAsync(CancellationToken ct)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = CashLedgerProjector.Project(envelopes);

        return state.Accounts.Values
            .OrderBy(a => a.IsArchived)
            .ThenBy(a => a.Name)
            .Select(a => new AccountListItemDto(
                a.AccountId,
                a.Name,
                a.AccountType,
                a.CurrencyCode,
                a.IsArchived,
                a.Balance
            ))
            .ToList();
    }
}
