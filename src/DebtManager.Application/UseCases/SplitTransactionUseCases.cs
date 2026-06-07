using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- DTOs ---
public sealed record SplitLineDto(string Category, decimal Amount, string? Notes);
public sealed record IncomeSplitLineDto(string Source, decimal Amount);

// --- Commands ---
public sealed record RecordSplitExpenseCommand(
    Guid AccountId,
    DateOnly EffectiveDate,
    decimal TotalAmount,
    string CurrencyCode,
    IReadOnlyList<SplitLineDto> Lines,
    string? Notes
);

public sealed record RecordSplitIncomeCommand(
    Guid AccountId,
    DateOnly EffectiveDate,
    decimal TotalAmount,
    string CurrencyCode,
    IReadOnlyList<IncomeSplitLineDto> Lines,
    string? Notes
);

public sealed record ReverseSplitExpenseCommand(
    Guid ParentId,
    string Reason,
    DateOnly EffectiveDate
);

public sealed record ReverseSplitIncomeCommand(
    Guid ParentId,
    string Reason,
    DateOnly EffectiveDate
);

// --- Handlers ---

public sealed class RecordSplitExpenseHandler
{
    private readonly IEventStore _store;

    public RecordSplitExpenseHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(
        RecordSplitExpenseCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        // Validation
        if (cmd.Lines == null || cmd.Lines.Count < 2)
            throw new InvalidOperationException("Split expense requires at least 2 lines.");

        if (cmd.TotalAmount <= 0)
            throw new InvalidOperationException("TotalAmount must be positive.");

        var currency = ResolveCurrency(cmd.CurrencyCode);

        var lineSum = 0m;
        var domainLines = new List<SplitLine>(cmd.Lines.Count);
        for (int i = 0; i < cmd.Lines.Count; i++)
        {
            var line = cmd.Lines[i];
            if (string.IsNullOrWhiteSpace(line.Category))
                throw new InvalidOperationException($"Split line {i} has an empty category.");
            if (line.Amount <= 0)
                throw new InvalidOperationException($"Split line {i} amount must be positive.");

            lineSum += line.Amount;
            domainLines.Add(new SplitLine(
                line.Category.Trim(),
                new Money(line.Amount, currency),
                line.Notes?.Trim()));
        }

        if (lineSum != cmd.TotalAmount)
            throw new InvalidOperationException(
                $"Sum of split lines ({lineSum}) does not equal TotalAmount ({cmd.TotalAmount}).");

        // Verify account exists
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var ledger = Domain.Projections.CashLedgerProjector.Project(all);
        if (!ledger.Accounts.TryGetValue(cmd.AccountId, out var account))
            throw new InvalidOperationException($"Account {cmd.AccountId} not found.");
        if (account.IsArchived)
            throw new InvalidOperationException($"Account '{account.Name}' is archived.");

        var parentId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var ev = new SplitExpenseRecorded(
            parentId,
            cmd.AccountId,
            new Money(cmd.TotalAmount, currency),
            cmd.EffectiveDate,
            domainLines,
            cmd.Notes?.Trim(),
            correlationId);

        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(cmd.AccountId),
            nameof(SplitExpenseRecorded),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            actorUserId,
            deviceId,
            correlationId,
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options));

        await _store.AppendAsync(env, ct);
    }

    private static Currency ResolveCurrency(string code) => code switch
    {
        "EGP" => Currency.EGP,
        "USD" => Currency.USD,
        "EUR" => Currency.EUR,
        _ => new Currency(code, 2)
    };
}

public sealed class RecordSplitIncomeHandler
{
    private readonly IEventStore _store;

    public RecordSplitIncomeHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(
        RecordSplitIncomeCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        // Validation
        if (cmd.Lines == null || cmd.Lines.Count < 2)
            throw new InvalidOperationException("Split income requires at least 2 lines.");

        if (cmd.TotalAmount <= 0)
            throw new InvalidOperationException("TotalAmount must be positive.");

        var currency = ResolveCurrency(cmd.CurrencyCode);

        var lineSum = 0m;
        var domainLines = new List<IncomeSplitLine>(cmd.Lines.Count);
        for (int i = 0; i < cmd.Lines.Count; i++)
        {
            var line = cmd.Lines[i];
            if (string.IsNullOrWhiteSpace(line.Source))
                throw new InvalidOperationException($"Split line {i} has an empty source.");
            if (line.Amount <= 0)
                throw new InvalidOperationException($"Split line {i} amount must be positive.");

            lineSum += line.Amount;
            domainLines.Add(new IncomeSplitLine(
                line.Source.Trim(),
                new Money(line.Amount, currency)));
        }

        if (lineSum != cmd.TotalAmount)
            throw new InvalidOperationException(
                $"Sum of split lines ({lineSum}) does not equal TotalAmount ({cmd.TotalAmount}).");

        // Verify account exists
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var ledger = Domain.Projections.CashLedgerProjector.Project(all);
        if (!ledger.Accounts.TryGetValue(cmd.AccountId, out var account))
            throw new InvalidOperationException($"Account {cmd.AccountId} not found.");
        if (account.IsArchived)
            throw new InvalidOperationException($"Account '{account.Name}' is archived.");

        var parentId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var ev = new SplitIncomeRecorded(
            parentId,
            cmd.AccountId,
            new Money(cmd.TotalAmount, currency),
            cmd.EffectiveDate,
            domainLines,
            cmd.Notes?.Trim(),
            correlationId);

        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(cmd.AccountId),
            nameof(SplitIncomeRecorded),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            actorUserId,
            deviceId,
            correlationId,
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options));

        await _store.AppendAsync(env, ct);
    }

    private static Currency ResolveCurrency(string code) => code switch
    {
        "EGP" => Currency.EGP,
        "USD" => Currency.USD,
        "EUR" => Currency.EUR,
        _ => new Currency(code, 2)
    };
}

public sealed class ReverseSplitExpenseHandler
{
    private readonly IEventStore _store;

    public ReverseSplitExpenseHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(
        ReverseSplitExpenseCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason))
            throw new InvalidOperationException("Reason is required for split expense reversal.");

        // Find original split event to get AccountId + TotalAmount
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var original = all
            .Where(e => e.EventType == nameof(SplitExpenseRecorded))
            .Select(e => JsonSerializer.Deserialize<SplitExpenseRecorded>(e.PayloadJson, DomainJson.Options))
            .FirstOrDefault(e => e != null && e.ParentId == cmd.ParentId);

        if (original == null)
            throw new InvalidOperationException($"Split expense with ParentId {cmd.ParentId} not found.");

        var correlationId = Guid.NewGuid();

        var ev = new SplitExpenseReversed(
            cmd.ParentId,
            original.AccountId,
            original.TotalAmount.Amount,
            cmd.Reason.Trim(),
            cmd.EffectiveDate,
            correlationId);

        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(original.AccountId),
            nameof(SplitExpenseReversed),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            actorUserId,
            deviceId,
            correlationId,
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options));

        await _store.AppendAsync(env, ct);
    }
}

public sealed class ReverseSplitIncomeHandler
{
    private readonly IEventStore _store;

    public ReverseSplitIncomeHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(
        ReverseSplitIncomeCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason))
            throw new InvalidOperationException("Reason is required for split income reversal.");

        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var original = all
            .Where(e => e.EventType == nameof(SplitIncomeRecorded))
            .Select(e => JsonSerializer.Deserialize<SplitIncomeRecorded>(e.PayloadJson, DomainJson.Options))
            .FirstOrDefault(e => e != null && e.ParentId == cmd.ParentId);

        if (original == null)
            throw new InvalidOperationException($"Split income with ParentId {cmd.ParentId} not found.");

        var correlationId = Guid.NewGuid();

        var ev = new SplitIncomeReversed(
            cmd.ParentId,
            original.AccountId,
            original.TotalAmount.Amount,
            cmd.Reason.Trim(),
            cmd.EffectiveDate,
            correlationId);

        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(original.AccountId),
            nameof(SplitIncomeReversed),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            actorUserId,
            deviceId,
            correlationId,
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options));

        await _store.AppendAsync(env, ct);
    }
}
