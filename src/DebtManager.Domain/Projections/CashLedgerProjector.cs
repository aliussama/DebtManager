using DebtManager.Domain.Events;
using DebtManager.Domain.Services.Serialization;

namespace DebtManager.Domain.Projections;

/// <summary>
/// Projects EventEnvelopes into CashLedgerState.
/// Deterministic: same inputs always produce same outputs.
/// Ordering: EffectiveDate ? OccurredAt ? EventId.
/// </summary>
public static class CashLedgerProjector
{
    public static CashLedgerState Project(IEnumerable<EventEnvelope> envelopes, DateOnly? asOfDate = null)
    {
        var state = new CashLedgerState();

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value)
            .ToList();

        foreach (var env in ordered)
        {
            if (asOfDate.HasValue && env.EffectiveDate > asOfDate.Value)
                continue;

            Apply(state, env);
        }

        return state;
    }

    private static void Apply(CashLedgerState state, EventEnvelope env)
    {
        var opt = DebtManager.Domain.ValueObjects.DomainJson.Options;

        switch (env.EventType)
        {
            case nameof(AccountCreated):
            {
                var ev = System.Text.Json.JsonSerializer.Deserialize<AccountCreated>(env.PayloadJson, opt);
                if (ev == null) return;

                var account = new AccountState
                {
                    AccountId = ev.AccountId,
                    Name = ev.Name,
                    AccountType = ev.AccountType,
                    CurrencyCode = ev.CurrencyCode,
                    Balance = ev.OpeningBalance,
                    CreatedDate = ev.EffectiveDate
                };
                state.Accounts[ev.AccountId] = account;

                if (ev.OpeningBalance != 0)
                {
                    state.Rows.Add(new CashLedgerRow
                    {
                        EventId = env.EventId.Value,
                        EffectiveDate = ev.EffectiveDate,
                        OccurredAt = env.OccurredAt,
                        AccountId = ev.AccountId,
                        AccountName = ev.Name,
                        Direction = ev.OpeningBalance >= 0 ? "In" : "Out",
                        Amount = Math.Abs(ev.OpeningBalance),
                        CurrencyCode = ev.CurrencyCode,
                        Category = "Opening Balance",
                        Reference = "Account Created",
                        CorrelationId = env.CorrelationId
                    });

                    if (ev.OpeningBalance > 0)
                        state.TotalIncome += ev.OpeningBalance;
                    else
                        state.TotalExpense += Math.Abs(ev.OpeningBalance);
                }
                break;
            }

            case nameof(AccountArchived):
            {
                var ev = System.Text.Json.JsonSerializer.Deserialize<AccountArchived>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Accounts.TryGetValue(ev.AccountId, out var account))
                    account.IsArchived = true;
                break;
            }

            case nameof(IncomeRecorded):
            {
                var ev = System.Text.Json.JsonSerializer.Deserialize<IncomeRecorded>(env.PayloadJson, opt);
                if (ev == null) return;

                EnsureDefaultAccount(state, ev.AccountId, ev.Amount.Currency.Code);

                if (state.Accounts.TryGetValue(ev.AccountId, out var account))
                    account.Balance += ev.Amount.Amount;

                state.TotalIncome += ev.Amount.Amount;

                state.Rows.Add(new CashLedgerRow
                {
                    EventId = env.EventId.Value,
                    EffectiveDate = ev.EffectiveDate,
                    OccurredAt = env.OccurredAt,
                    AccountId = ev.AccountId,
                    AccountName = state.Accounts.TryGetValue(ev.AccountId, out var a1) ? a1.Name : "Unknown",
                    Direction = "In",
                    Amount = ev.Amount.Amount,
                    CurrencyCode = ev.Amount.Currency.Code,
                    Category = "Income",
                    Reference = ev.Source,
                    CorrelationId = env.CorrelationId
                });
                break;
            }

            case nameof(IncomeReceived):
            {
                // Legacy event without AccountId — assign to default account
                var ev = System.Text.Json.JsonSerializer.Deserialize<IncomeReceived>(env.PayloadJson, opt);
                if (ev == null) return;

                var defaultId = DebtManager.Domain.Cash.DefaultAccount.AccountId;
                EnsureDefaultAccount(state, defaultId, ev.Amount.Currency.Code);

                if (state.Accounts.TryGetValue(defaultId, out var account))
                    account.Balance += ev.Amount.Amount;

                state.TotalIncome += ev.Amount.Amount;

                state.Rows.Add(new CashLedgerRow
                {
                    EventId = env.EventId.Value,
                    EffectiveDate = ev.EffectiveDate,
                    OccurredAt = env.OccurredAt,
                    AccountId = defaultId,
                    AccountName = state.Accounts.TryGetValue(defaultId, out var a2) ? a2.Name : "Default",
                    Direction = "In",
                    Amount = ev.Amount.Amount,
                    CurrencyCode = ev.Amount.Currency.Code,
                    Category = "Income",
                    Reference = ev.Source,
                    CorrelationId = env.CorrelationId
                });
                break;
            }

            case nameof(ExpenseRecorded):
            {
                var ev = System.Text.Json.JsonSerializer.Deserialize<ExpenseRecorded>(env.PayloadJson, opt);
                if (ev == null) return;

                EnsureDefaultAccount(state, ev.AccountId, ev.Amount.Currency.Code);

                if (state.Accounts.TryGetValue(ev.AccountId, out var account))
                    account.Balance -= ev.Amount.Amount;

                state.TotalExpense += ev.Amount.Amount;

                state.Rows.Add(new CashLedgerRow
                {
                    EventId = env.EventId.Value,
                    EffectiveDate = ev.EffectiveDate,
                    OccurredAt = env.OccurredAt,
                    AccountId = ev.AccountId,
                    AccountName = state.Accounts.TryGetValue(ev.AccountId, out var a3) ? a3.Name : "Unknown",
                    Direction = "Out",
                    Amount = ev.Amount.Amount,
                    CurrencyCode = ev.Amount.Currency.Code,
                    Category = ev.Category,
                    Reference = string.Empty,
                    Notes = ev.Notes,
                    CorrelationId = env.CorrelationId
                });
                break;
            }

            case nameof(SplitExpenseRecorded):
            {
                var ev = System.Text.Json.JsonSerializer.Deserialize<SplitExpenseRecorded>(env.PayloadJson, opt);
                if (ev == null) return;

                EnsureDefaultAccount(state, ev.AccountId, ev.TotalAmount.Currency.Code);

                if (state.Accounts.TryGetValue(ev.AccountId, out var splitExpAcct))
                    splitExpAcct.Balance -= ev.TotalAmount.Amount;

                state.TotalExpense += ev.TotalAmount.Amount;

                var acctNameSplitExp = state.Accounts.TryGetValue(ev.AccountId, out var aSplitExp) ? aSplitExp.Name : "Unknown";
                for (int i = 0; i < ev.Lines.Count; i++)
                {
                    var line = ev.Lines[i];
                    var lineNotes = string.IsNullOrEmpty(ev.Notes) && string.IsNullOrEmpty(line.Notes)
                        ? string.Empty
                        : string.Join(" | ", new[] { ev.Notes, line.Notes }.Where(n => !string.IsNullOrEmpty(n)));

                    state.Rows.Add(new CashLedgerRow
                    {
                        EventId = env.EventId.Value,
                        EffectiveDate = ev.EffectiveDate,
                        OccurredAt = env.OccurredAt,
                        AccountId = ev.AccountId,
                        AccountName = acctNameSplitExp,
                        Direction = "Out",
                        Amount = line.Amount.Amount,
                        CurrencyCode = line.Amount.Currency.Code,
                        Category = line.Category,
                        Reference = $"SplitExpense:{ev.ParentId}:{i}",
                        Notes = lineNotes,
                        CorrelationId = ev.CorrelationId
                    });
                }
                break;
            }

            case nameof(SplitIncomeRecorded):
            {
                var ev = System.Text.Json.JsonSerializer.Deserialize<SplitIncomeRecorded>(env.PayloadJson, opt);
                if (ev == null) return;

                EnsureDefaultAccount(state, ev.AccountId, ev.TotalAmount.Currency.Code);

                if (state.Accounts.TryGetValue(ev.AccountId, out var splitIncAcct))
                    splitIncAcct.Balance += ev.TotalAmount.Amount;

                state.TotalIncome += ev.TotalAmount.Amount;

                var acctNameSplitInc = state.Accounts.TryGetValue(ev.AccountId, out var aSplitInc) ? aSplitInc.Name : "Unknown";
                for (int i = 0; i < ev.Lines.Count; i++)
                {
                    var line = ev.Lines[i];
                    var lineNotes = string.IsNullOrEmpty(ev.Notes) && string.IsNullOrEmpty(line.Notes)
                        ? string.Empty
                        : string.Join(" | ", new[] { ev.Notes, line.Notes }.Where(n => !string.IsNullOrEmpty(n)));

                    state.Rows.Add(new CashLedgerRow
                    {
                        EventId = env.EventId.Value,
                        EffectiveDate = ev.EffectiveDate,
                        OccurredAt = env.OccurredAt,
                        AccountId = ev.AccountId,
                        AccountName = acctNameSplitInc,
                        Direction = "In",
                        Amount = line.Amount.Amount,
                        CurrencyCode = line.Amount.Currency.Code,
                        Category = "Income",
                        Reference = $"SplitIncome:{ev.ParentId}:{i} [{line.Source}]",
                        Notes = lineNotes,
                        CorrelationId = ev.CorrelationId
                    });
                }
                break;
            }

            case nameof(TransferRecorded):
            {
                var ev = System.Text.Json.JsonSerializer.Deserialize<TransferRecorded>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Accounts.TryGetValue(ev.FromAccountId, out var fromAccount))
                {
                    if (fromAccount.CurrencyCode != ev.CurrencyCode)
                        throw new InvalidOperationException($"Currency mismatch: account '{fromAccount.Name}' is {fromAccount.CurrencyCode}, transfer is {ev.CurrencyCode}");
                    fromAccount.Balance -= ev.Amount;
                }

                if (state.Accounts.TryGetValue(ev.ToAccountId, out var toAccount))
                {
                    if (toAccount.CurrencyCode != ev.CurrencyCode)
                        throw new InvalidOperationException($"Currency mismatch: account '{toAccount.Name}' is {toAccount.CurrencyCode}, transfer is {ev.CurrencyCode}");
                    toAccount.Balance += ev.Amount;
                }

                var fromName = fromAccount?.Name ?? "Unknown";
                var toName = toAccount?.Name ?? "Unknown";

                state.Rows.Add(new CashLedgerRow
                {
                    EventId = env.EventId.Value,
                    EffectiveDate = ev.EffectiveDate,
                    OccurredAt = env.OccurredAt,
                    AccountId = ev.FromAccountId,
                    AccountName = fromName,
                    Direction = "Transfer",
                    Amount = ev.Amount,
                    CurrencyCode = ev.CurrencyCode,
                    Reference = ev.Reference,
                    RelatedAccountId = ev.ToAccountId,
                    RelatedAccountName = toName,
                    CorrelationId = env.CorrelationId
                });
                break;
            }

            case nameof(IncomeReversed):
            {
                var ev = System.Text.Json.JsonSerializer.Deserialize<IncomeReversed>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Accounts.TryGetValue(ev.AccountId, out var account))
                    account.Balance -= ev.Amount;

                state.TotalIncome -= ev.Amount;

                state.Rows.Add(new CashLedgerRow
                {
                    EventId = env.EventId.Value,
                    EffectiveDate = ev.EffectiveDate,
                    OccurredAt = env.OccurredAt,
                    AccountId = ev.AccountId,
                    AccountName = state.Accounts.TryGetValue(ev.AccountId, out var a4) ? a4.Name : "Unknown",
                    Direction = "In",
                    Amount = -ev.Amount,
                    CurrencyCode = state.Accounts.TryGetValue(ev.AccountId, out var a4c) ? a4c.CurrencyCode : "EGP",
                    Category = "Income Reversal",
                    Reference = ev.Reason,
                    CorrelationId = env.CorrelationId
                });
                break;
            }

            case nameof(ExpenseReversed):
            {
                var ev = System.Text.Json.JsonSerializer.Deserialize<ExpenseReversed>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Accounts.TryGetValue(ev.AccountId, out var account))
                    account.Balance += ev.Amount;

                state.TotalExpense -= ev.Amount;

                state.Rows.Add(new CashLedgerRow
                {
                    EventId = env.EventId.Value,
                    EffectiveDate = ev.EffectiveDate,
                    OccurredAt = env.OccurredAt,
                    AccountId = ev.AccountId,
                    AccountName = state.Accounts.TryGetValue(ev.AccountId, out var a5) ? a5.Name : "Unknown",
                    Direction = "Out",
                    Amount = -ev.Amount,
                    CurrencyCode = state.Accounts.TryGetValue(ev.AccountId, out var a5c) ? a5c.CurrencyCode : "EGP",
                    Category = "Expense Reversal",
                    Reference = ev.Reason,
                    CorrelationId = env.CorrelationId
                });
                break;
            }

            case nameof(TransferReversed):
            {
                var ev = System.Text.Json.JsonSerializer.Deserialize<TransferReversed>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Accounts.TryGetValue(ev.FromAccountId, out var fromAcct))
                    fromAcct.Balance += ev.Amount;

                if (state.Accounts.TryGetValue(ev.ToAccountId, out var toAcct))
                    toAcct.Balance -= ev.Amount;

                var fromN = fromAcct?.Name ?? "Unknown";
                var toN = toAcct?.Name ?? "Unknown";

                state.Rows.Add(new CashLedgerRow
                {
                    EventId = env.EventId.Value,
                    EffectiveDate = ev.EffectiveDate,
                    OccurredAt = env.OccurredAt,
                    AccountId = ev.FromAccountId,
                    AccountName = fromN,
                    Direction = "Transfer",
                    Amount = -ev.Amount,
                    CurrencyCode = fromAcct?.CurrencyCode ?? "EGP",
                    Reference = ev.Reason,
                    RelatedAccountId = ev.ToAccountId,
                    RelatedAccountName = toN,
                    CorrelationId = env.CorrelationId
                });
                break;
            }

            case nameof(SplitExpenseReversed):
            {
                var ev = System.Text.Json.JsonSerializer.Deserialize<SplitExpenseReversed>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Accounts.TryGetValue(ev.AccountId, out var revAcct))
                    revAcct.Balance += ev.TotalAmount;

                state.TotalExpense -= ev.TotalAmount;

                // Find original split rows to negate per-category
                var originalRows = state.Rows
                    .Where(r => r.Reference.StartsWith($"SplitExpense:{ev.ParentId}:") && r.Amount > 0)
                    .ToList();

                var acctNameRevExp = state.Accounts.TryGetValue(ev.AccountId, out var aRevExp) ? aRevExp.Name : "Unknown";
                var currencyRevExp = aRevExp?.CurrencyCode ?? "EGP";

                if (originalRows.Count > 0)
                {
                    for (int i = 0; i < originalRows.Count; i++)
                    {
                        var orig = originalRows[i];
                        state.Rows.Add(new CashLedgerRow
                        {
                            EventId = env.EventId.Value,
                            EffectiveDate = ev.EffectiveDate,
                            OccurredAt = env.OccurredAt,
                            AccountId = ev.AccountId,
                            AccountName = acctNameRevExp,
                            Direction = "Out",
                            Amount = -orig.Amount,
                            CurrencyCode = currencyRevExp,
                            Category = orig.Category + " (Reversal)",
                            Reference = $"SplitExpenseReversal:{ev.ParentId}:{i}",
                            Notes = ev.Reason,
                            CorrelationId = ev.CorrelationId
                        });
                    }
                }
                else
                {
                    // Parent not found — emit single reversal marker row
                    state.Rows.Add(new CashLedgerRow
                    {
                        EventId = env.EventId.Value,
                        EffectiveDate = ev.EffectiveDate,
                        OccurredAt = env.OccurredAt,
                        AccountId = ev.AccountId,
                        AccountName = acctNameRevExp,
                        Direction = "Out",
                        Amount = -ev.TotalAmount,
                        CurrencyCode = currencyRevExp,
                        Category = "Split Expense Reversal",
                        Reference = $"SplitExpenseReversal:{ev.ParentId}",
                        Notes = ev.Reason,
                        CorrelationId = ev.CorrelationId
                    });
                }
                break;
            }

            case nameof(SplitIncomeReversed):
            {
                var ev = System.Text.Json.JsonSerializer.Deserialize<SplitIncomeReversed>(env.PayloadJson, opt);
                if (ev == null) return;

                if (state.Accounts.TryGetValue(ev.AccountId, out var revIncAcct))
                    revIncAcct.Balance -= ev.TotalAmount;

                state.TotalIncome -= ev.TotalAmount;

                var originalIncRows = state.Rows
                    .Where(r => r.Reference.StartsWith($"SplitIncome:{ev.ParentId}:") && r.Amount > 0)
                    .ToList();

                var acctNameRevInc = state.Accounts.TryGetValue(ev.AccountId, out var aRevInc) ? aRevInc.Name : "Unknown";
                var currencyRevInc = aRevInc?.CurrencyCode ?? "EGP";

                if (originalIncRows.Count > 0)
                {
                    for (int i = 0; i < originalIncRows.Count; i++)
                    {
                        var orig = originalIncRows[i];
                        state.Rows.Add(new CashLedgerRow
                        {
                            EventId = env.EventId.Value,
                            EffectiveDate = ev.EffectiveDate,
                            OccurredAt = env.OccurredAt,
                            AccountId = ev.AccountId,
                            AccountName = acctNameRevInc,
                            Direction = "In",
                            Amount = -orig.Amount,
                            CurrencyCode = currencyRevInc,
                            Category = "Income Reversal",
                            Reference = $"SplitIncomeReversal:{ev.ParentId}:{i}",
                            Notes = ev.Reason,
                            CorrelationId = ev.CorrelationId
                        });
                    }
                }
                else
                {
                    state.Rows.Add(new CashLedgerRow
                    {
                        EventId = env.EventId.Value,
                        EffectiveDate = ev.EffectiveDate,
                        OccurredAt = env.OccurredAt,
                        AccountId = ev.AccountId,
                        AccountName = acctNameRevInc,
                        Direction = "In",
                        Amount = -ev.TotalAmount,
                        CurrencyCode = currencyRevInc,
                        Category = "Income Reversal",
                        Reference = $"SplitIncomeReversal:{ev.ParentId}",
                        Notes = ev.Reason,
                        CorrelationId = ev.CorrelationId
                    });
                }
                break;
            }
        }
    }

    private static void EnsureDefaultAccount(CashLedgerState state, Guid accountId, string currencyCode)
    {
        if (!state.Accounts.ContainsKey(accountId))
        {
            state.Accounts[accountId] = new AccountState
            {
                AccountId = accountId,
                Name = accountId == DebtManager.Domain.Cash.DefaultAccount.AccountId ? "Default" : "Auto",
                AccountType = "Cash",
                CurrencyCode = currencyCode,
                Balance = 0m,
                CreatedDate = DateOnly.MinValue
            };
        }
    }
}
