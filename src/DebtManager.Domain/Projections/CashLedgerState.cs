using DebtManager.Domain.Events;

namespace DebtManager.Domain.Projections;

/// <summary>
/// State of a single account derived from events.
/// </summary>
public sealed class AccountState
{
    public Guid AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public bool IsArchived { get; set; }
    public DateOnly CreatedDate { get; set; }
}

/// <summary>
/// A single row in the cash ledger.
/// </summary>
public sealed class CashLedgerRow
{
    public Guid EventId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty; // "In", "Out", "Transfer"
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public Guid? RelatedAccountId { get; set; }
    public string RelatedAccountName { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public Guid? SourceId { get; set; }
}

/// <summary>
/// Full cash ledger state derived deterministically from events.
/// </summary>
public sealed class CashLedgerState
{
    public Dictionary<Guid, AccountState> Accounts { get; } = new();
    public List<CashLedgerRow> Rows { get; } = new();
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal NetCashflow => TotalIncome - TotalExpense;
}
