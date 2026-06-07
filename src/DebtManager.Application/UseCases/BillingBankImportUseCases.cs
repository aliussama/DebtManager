using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Commands ---

public sealed record ApplyImportedTransactionToBillPaymentCommand(
    Guid ImportedId, Guid BillId, Guid AccountId, string Method, string? Notes);

public sealed record ApplyImportedTransactionToInvoicePaymentCommand(
    Guid ImportedId, Guid InvoiceId, Guid AccountId, string Method, string? Notes);

// --- Handlers ---

public sealed class ApplyImportedTransactionToBillPaymentHandler
{
    private readonly IEventStore _store;
    public ApplyImportedTransactionToBillPaymentHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ApplyImportedTransactionToBillPaymentCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var importState = BankImportProjector.Project(all);
        var billingState = BillingProjector.Project(all);

        if (!importState.ImportedTransactions.TryGetValue(cmd.ImportedId, out var imported))
            throw new InvalidOperationException("Imported transaction not found");
        if (!billingState.Bills.TryGetValue(cmd.BillId, out var bill))
            throw new InvalidOperationException("Bill not found");
        if (importState.AppliedLinks.ContainsKey(cmd.ImportedId))
            throw new InvalidOperationException("Transaction already applied");

        var correlationId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var effectiveDate = imported.TxnDate;
        var currency = new Currency(imported.CurrencyCode, 2);

        // 1. Record bill payment
        var billPayment = new BillPaymentRecorded(cmd.BillId, paymentId, cmd.AccountId,
            imported.Amount, imported.CurrencyCode, effectiveDate, cmd.Method,
            $"Import:{cmd.ImportedId}", effectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(BillPaymentRecorded), DateTimeOffset.UtcNow, effectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(billPayment, DomainJson.Options)), ct);

        // 2. Record cash expense
        var expenseEventId = Guid.NewGuid();
        var expense = new ExpenseRecorded(cmd.AccountId, new Money(imported.Amount, currency),
            effectiveDate, "Bill Payment", cmd.Notes ?? $"Bill Payment: {cmd.BillId}");
        await _store.AppendAsync(new EventEnvelope(
            new EventId(expenseEventId), new StreamId(cmd.AccountId),
            nameof(ExpenseRecorded), DateTimeOffset.UtcNow, effectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(expense, DomainJson.Options)), ct);

        // 3. Link imported transaction
        var applied = new BankTransactionApplied(cmd.ImportedId, expenseEventId,
            "BillPaymentRecorded", effectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportStreams.ImportStream,
            nameof(BankTransactionApplied), DateTimeOffset.UtcNow, effectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(applied, DomainJson.Options)), ct);
    }
}

public sealed class ApplyImportedTransactionToInvoicePaymentHandler
{
    private readonly IEventStore _store;
    public ApplyImportedTransactionToInvoicePaymentHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ApplyImportedTransactionToInvoicePaymentCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var importState = BankImportProjector.Project(all);
        var billingState = BillingProjector.Project(all);

        if (!importState.ImportedTransactions.TryGetValue(cmd.ImportedId, out var imported))
            throw new InvalidOperationException("Imported transaction not found");
        if (!billingState.Invoices.TryGetValue(cmd.InvoiceId, out var invoice))
            throw new InvalidOperationException("Invoice not found");
        if (importState.AppliedLinks.ContainsKey(cmd.ImportedId))
            throw new InvalidOperationException("Transaction already applied");

        var correlationId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var effectiveDate = imported.TxnDate;
        var currency = new Currency(imported.CurrencyCode, 2);

        // 1. Record invoice payment
        var invPayment = new InvoicePaymentRecorded(cmd.InvoiceId, paymentId, cmd.AccountId,
            imported.Amount, imported.CurrencyCode, effectiveDate, cmd.Method,
            $"Import:{cmd.ImportedId}", effectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
            nameof(InvoicePaymentRecorded), DateTimeOffset.UtcNow, effectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(invPayment, DomainJson.Options)), ct);

        // 2. Record cash income
        var incomeEventId = Guid.NewGuid();
        var income = new IncomeRecorded(cmd.AccountId, new Money(imported.Amount, currency),
            effectiveDate, cmd.Notes ?? $"Invoice Payment: {cmd.InvoiceId}");
        await _store.AppendAsync(new EventEnvelope(
            new EventId(incomeEventId), new StreamId(cmd.AccountId),
            nameof(IncomeRecorded), DateTimeOffset.UtcNow, effectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(income, DomainJson.Options)), ct);

        // 3. Link imported transaction
        var applied = new BankTransactionApplied(cmd.ImportedId, incomeEventId,
            "InvoicePaymentRecorded", effectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportStreams.ImportStream,
            nameof(BankTransactionApplied), DateTimeOffset.UtcNow, effectiveDate,
            actorUserId, deviceId, correlationId, null, 1,
            JsonSerializer.Serialize(applied, DomainJson.Options)), ct);
    }
}
