using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Billing;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class BillingAndContractsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public BillingAndContractsTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"BillingTests_{id}.db");
        _factory = new SqliteConnectionFactory(_dbPath, new TestKeyStore());
        _eventStore = new SqliteEventStore(_factory);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        for (int i = 0; i < 30; i++)
        {
            try
            {
                if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
                if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
                if (File.Exists(_dbPath)) File.Delete(_dbPath);
                break;
            }
            catch (IOException) when (i < 29) { Thread.Sleep(100); }
        }
    }

    // ----------------------------------------------------------------
    // 1) Parties_CreateModifyArchive_Works
    // ----------------------------------------------------------------
    [Fact]
    public async Task Parties_CreateModifyArchive_Works()
    {
        var createHandler = new CreatePartyHandler(_eventStore);
        var modifyHandler = new ModifyPartyHandler(_eventStore);
        var archiveHandler = new ArchivePartyHandler(_eventStore);
        var listHandler = new GetPartiesListHandler(_eventStore);

        // Create
        var partyId = await createHandler.HandleAsync(
            new CreatePartyCommand(null, "Vendor", "ACME Corp", "EGP", null, ["utilities"],
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var parties = await listHandler.HandleAsync(false, CancellationToken.None);
        Assert.Single(parties);
        Assert.Equal("ACME Corp", parties[0].Name);
        Assert.Equal("Vendor", parties[0].Kind);

        // Modify
        await modifyHandler.HandleAsync(
            new ModifyPartyCommand(partyId, "ACME Inc", "USD", null, ["utilities", "preferred"],
                new DateOnly(2025, 2, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        parties = await listHandler.HandleAsync(false, CancellationToken.None);
        Assert.Equal("ACME Inc", parties[0].Name);
        Assert.Equal("USD", parties[0].DefaultCurrencyCode);

        // Archive
        await archiveHandler.HandleAsync(
            new ArchivePartyCommand(partyId, "No longer active", new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        parties = await listHandler.HandleAsync(false, CancellationToken.None);
        Assert.Empty(parties);

        parties = await listHandler.HandleAsync(true, CancellationToken.None);
        Assert.Single(parties);
        Assert.True(parties[0].IsArchived);
    }

    // ----------------------------------------------------------------
    // 2) Contracts_CreateModifyArchive_Works
    // ----------------------------------------------------------------
    [Fact]
    public async Task Contracts_CreateModifyArchive_Works()
    {
        var partyId = await CreateParty("Test Vendor", "Vendor");
        var createHandler = new CreateContractHandler(_eventStore);
        var modifyHandler = new ModifyContractHandler(_eventStore);
        var archiveHandler = new ArchiveContractHandler(_eventStore);
        var listHandler = new GetContractsListHandler(_eventStore);

        var termsJson = JsonSerializer.Serialize(new { BillingCycle = "Monthly", BillingInterval = 1, BillingDayOfMonth = 1, BaseAmount = 500m, Category = "Subscription" });

        var contractId = await createHandler.HandleAsync(
            new CreateContractCommand(null, partyId, "Subscription", "Netflix",
                new DateOnly(2025, 1, 1), null, "EGP", termsJson, new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var contracts = await listHandler.HandleAsync(false, CancellationToken.None);
        Assert.Single(contracts);
        Assert.Equal("Netflix", contracts[0].Title);

        // Modify
        await modifyHandler.HandleAsync(
            new ModifyContractCommand(contractId, "Netflix Premium", new DateOnly(2026, 12, 31), termsJson,
                new DateOnly(2025, 2, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        contracts = await listHandler.HandleAsync(false, CancellationToken.None);
        Assert.Equal("Netflix Premium", contracts[0].Title);

        // Archive
        await archiveHandler.HandleAsync(
            new ArchiveContractCommand(contractId, "Cancelled subscription", new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        contracts = await listHandler.HandleAsync(false, CancellationToken.None);
        Assert.Empty(contracts);
    }

    // ----------------------------------------------------------------
    // 3) ContractGeneration_Preview_DoesNotWriteEvents
    // ----------------------------------------------------------------
    [Fact]
    public async Task ContractGeneration_Preview_DoesNotWriteEvents()
    {
        var partyId = await CreateParty("ISP", "Vendor");
        var termsJson = JsonSerializer.Serialize(new { BillingCycle = "Monthly", BillingInterval = 1, BillingDayOfMonth = 15, BaseAmount = 300m, Category = "Internet" });

        var createHandler = new CreateContractHandler(_eventStore);
        var contractId = await createHandler.HandleAsync(
            new CreateContractCommand(null, partyId, "Utilities", "Internet Service",
                new DateOnly(2025, 1, 1), null, "EGP", termsJson, new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var previewHandler = new PreviewContractBillingGenerationHandler(_eventStore);
        var eventsBefore = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var countBefore = eventsBefore.Count;

        var preview = await previewHandler.HandleAsync(
            new PreviewContractBillingGenerationCommand(contractId, new DateOnly(2025, 6, 1)),
            CancellationToken.None);

        var eventsAfter = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        Assert.Equal(countBefore, eventsAfter.Count);
        Assert.True(preview.Candidates.Count > 0);
    }

    // ----------------------------------------------------------------
    // 4) ContractGeneration_GenerateBills_IsIdempotent_ByCycleKey
    // ----------------------------------------------------------------
    [Fact]
    public async Task ContractGeneration_GenerateBills_IsIdempotent_ByCycleKey()
    {
        var partyId = await CreateParty("Landlord", "Vendor");
        var termsJson = JsonSerializer.Serialize(new { BillingCycle = "Monthly", BillingInterval = 1, BillingDayOfMonth = 1, BaseAmount = 2000m, Category = "Rent" });

        var createHandler = new CreateContractHandler(_eventStore);
        var contractId = await createHandler.HandleAsync(
            new CreateContractCommand(null, partyId, "Rent", "Office Rent",
                new DateOnly(2025, 1, 1), null, "EGP", termsJson, new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var genHandler = new GenerateContractBillsHandler(_eventStore);

        // Generate first time
        var count1 = await genHandler.HandleAsync(
            new GenerateContractBillsCommand(contractId, new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
        Assert.True(count1 > 0);

        // Generate again (idempotent)
        var count2 = await genHandler.HandleAsync(
            new GenerateContractBillsCommand(contractId, new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1)),
            _actorUserId, _deviceId, CancellationToken.None);
        Assert.Equal(0, count2);
    }

    // ----------------------------------------------------------------
    // 5) BillIssued_StatusDue_OutstandingCorrect
    // ----------------------------------------------------------------
    [Fact]
    public async Task BillIssued_StatusDue_OutstandingCorrect()
    {
        var partyId = await CreateParty("Supplier", "Vendor");
        var issueHandler = new IssueBillHandler(_eventStore);
        var dashHandler = new GetBillingDashboardHandler(_eventStore);

        await issueHandler.HandleAsync(
            new IssueBillCommand(null, null, partyId, "EGP", 1000m,
                new DateOnly(2025, 3, 15), "Supplies", "INV-001", null,
                new DateOnly(2025, 2, 15)),
            _actorUserId, _deviceId, CancellationToken.None);

        var dash = await dashHandler.HandleAsync(new DateOnly(2025, 3, 1), false, CancellationToken.None);
        Assert.Single(dash.Bills);
        Assert.Equal("Due", dash.Bills[0].Status);
        Assert.Equal(1000m, dash.Bills[0].Outstanding);
        Assert.Equal(0m, dash.Bills[0].TotalPaid);
    }

    // ----------------------------------------------------------------
    // 6) BillPayment_PartialPayment_StatusPartiallyPaid
    // ----------------------------------------------------------------
    [Fact]
    public async Task BillPayment_PartialPayment_StatusPartiallyPaid()
    {
        var partyId = await CreateParty("Vendor A", "Vendor");
        var accountId = await CreateAccount("Cash", "EGP");
        var issueHandler = new IssueBillHandler(_eventStore);
        var payHandler = new RecordBillPaymentHandler(_eventStore);
        var dashHandler = new GetBillingDashboardHandler(_eventStore);

        var billId = await issueHandler.HandleAsync(
            new IssueBillCommand(null, null, partyId, "EGP", 1000m,
                new DateOnly(2025, 3, 15), "General", "BILL-002", null,
                new DateOnly(2025, 2, 15)),
            _actorUserId, _deviceId, CancellationToken.None);

        await payHandler.HandleAsync(
            new RecordBillPaymentCommand(billId, null, accountId, 400m, "EGP",
                new DateOnly(2025, 3, 1), "Manual", null, new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var dash = await dashHandler.HandleAsync(new DateOnly(2025, 3, 10), false, CancellationToken.None);
        Assert.Single(dash.Bills);
        Assert.Equal("PartiallyPaid", dash.Bills[0].Status);
        Assert.Equal(600m, dash.Bills[0].Outstanding);
        Assert.Equal(400m, dash.Bills[0].TotalPaid);
    }

    // ----------------------------------------------------------------
    // 7) BillPayment_FullPayment_StatusPaid_AndCashExpenseRecorded
    // ----------------------------------------------------------------
    [Fact]
    public async Task BillPayment_FullPayment_StatusPaid_AndCashExpenseRecorded()
    {
        var partyId = await CreateParty("Vendor B", "Vendor");
        var accountId = await CreateAccount("Checking", "EGP");
        var issueHandler = new IssueBillHandler(_eventStore);
        var payHandler = new RecordBillPaymentHandler(_eventStore);
        var dashHandler = new GetBillingDashboardHandler(_eventStore);

        var billId = await issueHandler.HandleAsync(
            new IssueBillCommand(null, null, partyId, "EGP", 500m,
                new DateOnly(2025, 4, 1), "Utilities", "BILL-003", null,
                new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        await payHandler.HandleAsync(
            new RecordBillPaymentCommand(billId, null, accountId, 500m, "EGP",
                new DateOnly(2025, 3, 15), "Manual", null, new DateOnly(2025, 3, 15)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Bill should be Paid
        var dash = await dashHandler.HandleAsync(new DateOnly(2025, 4, 1), true, CancellationToken.None);
        var bill = dash.Bills.FirstOrDefault(b => b.BillId == billId);
        Assert.NotNull(bill);
        Assert.Equal("Paid", bill!.Status);
        Assert.Equal(0m, bill.Outstanding);

        // Cash ledger should have the expense
        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var cashState = CashLedgerProjector.Project(envelopes);
        var expenses = cashState.Rows.Where(r => r.Direction == "Out" && r.Notes.Contains("Bill Payment")).ToList();
        Assert.NotEmpty(expenses);
    }

    // ----------------------------------------------------------------
    // 8) BillPayment_Unapply_RestoresOutstanding
    // ----------------------------------------------------------------
    [Fact]
    public async Task BillPayment_Unapply_RestoresOutstanding()
    {
        var partyId = await CreateParty("Vendor C", "Vendor");
        var accountId = await CreateAccount("Cash", "EGP");
        var issueHandler = new IssueBillHandler(_eventStore);
        var payHandler = new RecordBillPaymentHandler(_eventStore);
        var unapplyHandler = new UnapplyBillPaymentHandler(_eventStore);

        var billId = await issueHandler.HandleAsync(
            new IssueBillCommand(null, null, partyId, "EGP", 800m,
                new DateOnly(2025, 4, 1), "General", "BILL-004", null,
                new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var paymentId = await payHandler.HandleAsync(
            new RecordBillPaymentCommand(billId, null, accountId, 800m, "EGP",
                new DateOnly(2025, 3, 15), "Manual", null, new DateOnly(2025, 3, 15)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Unapply
        await unapplyHandler.HandleAsync(
            new UnapplyBillPaymentCommand(billId, paymentId, "Wrong bill",
                new DateOnly(2025, 3, 16)),
            _actorUserId, _deviceId, CancellationToken.None);

        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var billingState = BillingProjector.Project(envelopes);
        var bill = billingState.Bills[billId];
        Assert.Equal(800m, bill.Outstanding);
        Assert.Equal("Due", bill.Status);
    }

    // ----------------------------------------------------------------
    // 9) BillPayment_Reverse_AppendsReversal_AndRestoresCash
    // ----------------------------------------------------------------
    [Fact]
    public async Task BillPayment_Reverse_AppendsReversal_AndRestoresCash()
    {
        var partyId = await CreateParty("Vendor D", "Vendor");
        var accountId = await CreateAccount("Cash", "EGP");
        var issueHandler = new IssueBillHandler(_eventStore);
        var payHandler = new RecordBillPaymentHandler(_eventStore);
        var reverseHandler = new ReverseBillPaymentHandler(_eventStore);

        var billId = await issueHandler.HandleAsync(
            new IssueBillCommand(null, null, partyId, "EGP", 600m,
                new DateOnly(2025, 4, 1), "General", "BILL-005", null,
                new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var paymentId = await payHandler.HandleAsync(
            new RecordBillPaymentCommand(billId, null, accountId, 600m, "EGP",
                new DateOnly(2025, 3, 15), "Manual", null, new DateOnly(2025, 3, 15)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Reverse
        await reverseHandler.HandleAsync(
            new ReverseBillPaymentCommand(billId, paymentId, "Duplicate payment",
                new DateOnly(2025, 3, 16)),
            _actorUserId, _deviceId, CancellationToken.None);

        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var billingState = BillingProjector.Project(envelopes);
        var bill = billingState.Bills[billId];
        Assert.Equal(600m, bill.Outstanding);

        // Reversal event exists
        var reversalEvents = envelopes.Where(e => e.EventType == nameof(BillPaymentReversed)).ToList();
        Assert.Single(reversalEvents);
    }

    // ----------------------------------------------------------------
    // 10) InvoiceIssued_AndPayment_CreatesIncomeRecorded
    // ----------------------------------------------------------------
    [Fact]
    public async Task InvoiceIssued_AndPayment_CreatesIncomeRecorded()
    {
        var partyId = await CreateParty("Customer X", "Customer");
        var accountId = await CreateAccount("Revenue", "EGP");
        var issueHandler = new IssueInvoiceHandler(_eventStore);
        var payHandler = new RecordInvoicePaymentHandler(_eventStore);

        var invoiceId = await issueHandler.HandleAsync(
            new IssueInvoiceCommand(null, null, partyId, "EGP", 2000m,
                new DateOnly(2025, 4, 1), "Services", "INV-001", null,
                new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        await payHandler.HandleAsync(
            new RecordInvoicePaymentCommand(invoiceId, null, accountId, 2000m, "EGP",
                new DateOnly(2025, 3, 20), "BankTransfer", null, new DateOnly(2025, 3, 20)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Invoice paid
        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var billingState = BillingProjector.Project(envelopes);
        Assert.Equal("Paid", billingState.Invoices[invoiceId].Status);

        // Income recorded
        var cashState = CashLedgerProjector.Project(envelopes);
        var incomes = cashState.Rows.Where(r => r.Direction == "In" && r.Reference.Contains("Invoice Payment")).ToList();
        Assert.NotEmpty(incomes);
    }

    // ----------------------------------------------------------------
    // 11) Invoice_Overpayment_ShowsZeroOutstanding
    // ----------------------------------------------------------------
    [Fact]
    public async Task Invoice_Overpayment_ShowsZeroOutstanding()
    {
        var partyId = await CreateParty("Customer Y", "Customer");
        var accountId = await CreateAccount("Cash", "EGP");
        var issueHandler = new IssueInvoiceHandler(_eventStore);
        var payHandler = new RecordInvoicePaymentHandler(_eventStore);

        var invoiceId = await issueHandler.HandleAsync(
            new IssueInvoiceCommand(null, null, partyId, "EGP", 1000m,
                new DateOnly(2025, 4, 1), "Services", "INV-002", null,
                new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Pay more than owed
        await payHandler.HandleAsync(
            new RecordInvoicePaymentCommand(invoiceId, null, accountId, 1200m, "EGP",
                new DateOnly(2025, 3, 20), "Manual", null, new DateOnly(2025, 3, 20)),
            _actorUserId, _deviceId, CancellationToken.None);

        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var billingState = BillingProjector.Project(envelopes);
        var inv = billingState.Invoices[invoiceId];
        Assert.Equal("Paid", inv.Status);
        // Outstanding clamped to 0 (overpayment = credit, but outstanding doesn't go negative)
        Assert.Equal(0m, inv.Outstanding);
    }

    // ----------------------------------------------------------------
    // 12) Adjustment_AddAndReverse_ChangesOutstanding
    // ----------------------------------------------------------------
    [Fact]
    public async Task Adjustment_AddAndReverse_ChangesOutstanding()
    {
        var partyId = await CreateParty("Vendor E", "Vendor");
        var issueHandler = new IssueBillHandler(_eventStore);
        var adjustHandler = new AddBillAdjustmentHandler(_eventStore);
        var reverseAdjHandler = new ReverseBillAdjustmentHandler(_eventStore);

        var billId = await issueHandler.HandleAsync(
            new IssueBillCommand(null, null, partyId, "EGP", 1000m,
                new DateOnly(2025, 4, 1), "General", "BILL-006", null,
                new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Add discount
        var adjId = await adjustHandler.HandleAsync(
            new AddBillAdjustmentCommand(billId, null, "Discount", 200m, "Loyalty discount",
                new DateOnly(2025, 3, 5)),
            _actorUserId, _deviceId, CancellationToken.None);

        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state = BillingProjector.Project(envelopes);
        Assert.Equal(800m, state.Bills[billId].Outstanding); // 1000 - 200 discount

        // Reverse discount
        await reverseAdjHandler.HandleAsync(
            new ReverseBillAdjustmentCommand(billId, adjId, "Discount revoked",
                new DateOnly(2025, 3, 6)),
            _actorUserId, _deviceId, CancellationToken.None);

        envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        state = BillingProjector.Project(envelopes);
        Assert.Equal(1000m, state.Bills[billId].Outstanding);
    }

    // ----------------------------------------------------------------
    // 13) AgingReport_BucketsCorrect
    // ----------------------------------------------------------------
    [Fact]
    public async Task AgingReport_BucketsCorrect()
    {
        var partyId = await CreateParty("Vendor F", "Vendor");
        var issueHandler = new IssueBillHandler(_eventStore);
        var agingHandler = new GetAgingReportHandler(_eventStore);

        // Bill due today (Current)
        await issueHandler.HandleAsync(
            new IssueBillCommand(null, null, partyId, "EGP", 100m,
                new DateOnly(2025, 6, 1), "General", "CURR", null,
                new DateOnly(2025, 5, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Bill 15 days overdue (0-30 bucket)
        await issueHandler.HandleAsync(
            new IssueBillCommand(null, null, partyId, "EGP", 200m,
                new DateOnly(2025, 5, 16), "General", "OD15", null,
                new DateOnly(2025, 5, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Bill 45 days overdue (31-60 bucket)
        await issueHandler.HandleAsync(
            new IssueBillCommand(null, null, partyId, "EGP", 300m,
                new DateOnly(2025, 4, 17), "General", "OD45", null,
                new DateOnly(2025, 4, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var asOf = new DateOnly(2025, 6, 1);
        var aging = await agingHandler.HandleAsync(asOf, CancellationToken.None);

        var current = aging.First(r => r.Bucket == "Current");
        Assert.Equal(1, current.BillCount);
        Assert.Equal(100m, current.BillAmount);

        var bucket030 = aging.First(r => r.Bucket == "0-30");
        Assert.Equal(1, bucket030.BillCount);
        Assert.Equal(200m, bucket030.BillAmount);

        var bucket3160 = aging.First(r => r.Bucket == "31-60");
        Assert.Equal(1, bucket3160.BillCount);
        Assert.Equal(300m, bucket3160.BillAmount);
    }

    // ----------------------------------------------------------------
    // 14) BankImport_ApplyToBillPayment_WritesLinkedEvents_AndAppearsInLedger
    // ----------------------------------------------------------------
    [Fact]
    public async Task BankImport_ApplyToBillPayment_WritesLinkedEvents_AndAppearsInLedger()
    {
        var partyId = await CreateParty("Vendor G", "Vendor");
        var accountId = await CreateAccount("Bank", "EGP");

        // Issue a bill
        var issueHandler = new IssueBillHandler(_eventStore);
        var billId = await issueHandler.HandleAsync(
            new IssueBillCommand(null, null, partyId, "EGP", 750m,
                new DateOnly(2025, 4, 1), "General", "BILL-IMPORT", null,
                new DateOnly(2025, 3, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Simulate an imported bank transaction
        var importedId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var txnEvent = new BankTransactionImported(importedId, batchId, accountId,
            new DateOnly(2025, 3, 20), 750m, "EGP", "Payment to Vendor G",
            "REF123", "Vendor G", "Out", "{}", new DateOnly(2025, 3, 20));
        await _eventStore.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), ImportStreams.ImportStream,
            nameof(BankTransactionImported), DateTimeOffset.UtcNow, txnEvent.EffectiveDate,
            _actorUserId, _deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(txnEvent, DomainJson.Options)), CancellationToken.None);

        // Apply imported transaction to bill payment
        var applyHandler = new ApplyImportedTransactionToBillPaymentHandler(_eventStore);
        await applyHandler.HandleAsync(
            new ApplyImportedTransactionToBillPaymentCommand(importedId, billId, accountId, "BankTransfer", null),
            _actorUserId, _deviceId, CancellationToken.None);

        // Verify bill is paid
        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var billingState = BillingProjector.Project(envelopes);
        Assert.Equal("Paid", billingState.Bills[billId].Status);

        // Verify BankTransactionApplied event
        var appliedEvents = envelopes.Where(e => e.EventType == nameof(BankTransactionApplied)).ToList();
        Assert.NotEmpty(appliedEvents);

        // Verify expense appears in cash ledger
        var cashState = CashLedgerProjector.Project(envelopes);
        var expenses = cashState.Rows.Where(r => r.Direction == "Out" && r.Notes.Contains("Bill Payment")).ToList();
        Assert.NotEmpty(expenses);

        // Verify import state has the link
        var importState = BankImportProjector.Project(envelopes);
        Assert.True(importState.AppliedLinks.ContainsKey(importedId));
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private async Task<Guid> CreateParty(string name, string kind)
    {
        var handler = new CreatePartyHandler(_eventStore);
        return await handler.HandleAsync(
            new CreatePartyCommand(null, kind, name, "EGP", null, [],
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> CreateAccount(string name, string ccy)
    {
        var accountId = Guid.NewGuid();
        var ev = new AccountCreated(accountId, name, "Cash", ccy, 0m, DateOnly.FromDateTime(DateTime.Today));
        await _eventStore.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(accountId),
            nameof(AccountCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            _actorUserId, _deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), CancellationToken.None);
        return accountId;
    }
}
