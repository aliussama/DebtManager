using System.Text.Json;
using DebtManager.Domain.Billing;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Commands ---

public sealed record PreviewContractBillingGenerationCommand(Guid ContractId, DateOnly AsOfDate);

public sealed record GenerateContractBillsCommand(Guid ContractId, DateOnly AsOfDate, DateOnly EffectiveDate);

public sealed record GenerateContractInvoicesCommand(Guid ContractId, DateOnly AsOfDate, DateOnly EffectiveDate);

// --- DTOs ---

public sealed record ContractBillingPreviewDto(
    Guid ContractId,
    IReadOnlyList<ContractBillingCandidate> Candidates,
    int AlreadyGeneratedCount);

// --- Handlers ---

public sealed class PreviewContractBillingGenerationHandler
{
    private readonly IEventStore _store;
    public PreviewContractBillingGenerationHandler(IEventStore store) => _store = store;

    public async Task<ContractBillingPreviewDto> HandleAsync(PreviewContractBillingGenerationCommand cmd, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var contractState = ContractsProjector.Project(all);
        var billingState = BillingProjector.Project(all);

        if (!contractState.Contracts.TryGetValue(cmd.ContractId, out var contract))
            throw new InvalidOperationException("Contract not found");

        var existingKeys = billingState.GeneratedCycleKeys.TryGetValue(cmd.ContractId, out var keys)
            ? (IReadOnlySet<string>)keys : new HashSet<string>();

        var candidates = ContractBillingGenerator.GenerateCandidates(
            contract.ContractId, contract.StartDate, contract.EndDate,
            contract.CurrencyCode, contract.TermsJson, cmd.AsOfDate, existingKeys);

        return new ContractBillingPreviewDto(cmd.ContractId, candidates, existingKeys.Count);
    }
}

public sealed class GenerateContractBillsHandler
{
    private readonly IEventStore _store;
    public GenerateContractBillsHandler(IEventStore store) => _store = store;

    public async Task<int> HandleAsync(GenerateContractBillsCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var contractState = ContractsProjector.Project(all);
        var billingState = BillingProjector.Project(all);
        var partyState = PartiesProjector.Project(all);

        if (!contractState.Contracts.TryGetValue(cmd.ContractId, out var contract))
            throw new InvalidOperationException("Contract not found");

        var existingKeys = billingState.GeneratedCycleKeys.TryGetValue(cmd.ContractId, out var keys)
            ? (IReadOnlySet<string>)keys : new HashSet<string>();

        var candidates = ContractBillingGenerator.GenerateCandidates(
            contract.ContractId, contract.StartDate, contract.EndDate,
            contract.CurrencyCode, contract.TermsJson, cmd.AsOfDate, existingKeys);

        int count = 0;
        foreach (var candidate in candidates)
        {
            var billId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();

            // 1. Issue the bill
            var billEv = new BillIssued(billId, contract.ContractId, contract.PartyId,
                candidate.CurrencyCode, candidate.Amount, candidate.DueDate,
                candidate.Category, candidate.Reference, null, cmd.EffectiveDate);
            await _store.AppendAsync(new EventEnvelope(
                new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
                nameof(BillIssued), DateTimeOffset.UtcNow, cmd.EffectiveDate,
                actorUserId, deviceId, correlationId, null, 1,
                JsonSerializer.Serialize(billEv, DomainJson.Options)), ct);

            // 2. Record generation link
            var genEv = new ContractBillGenerated(contract.ContractId, billId, candidate.CycleKey, cmd.EffectiveDate);
            await _store.AppendAsync(new EventEnvelope(
                new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
                nameof(ContractBillGenerated), DateTimeOffset.UtcNow, cmd.EffectiveDate,
                actorUserId, deviceId, correlationId, null, 1,
                JsonSerializer.Serialize(genEv, DomainJson.Options)), ct);

            count++;
        }

        return count;
    }
}

public sealed class GenerateContractInvoicesHandler
{
    private readonly IEventStore _store;
    public GenerateContractInvoicesHandler(IEventStore store) => _store = store;

    public async Task<int> HandleAsync(GenerateContractInvoicesCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var contractState = ContractsProjector.Project(all);
        var billingState = BillingProjector.Project(all);

        if (!contractState.Contracts.TryGetValue(cmd.ContractId, out var contract))
            throw new InvalidOperationException("Contract not found");

        var existingKeys = billingState.GeneratedCycleKeys.TryGetValue(cmd.ContractId, out var keys)
            ? (IReadOnlySet<string>)keys : new HashSet<string>();

        var candidates = ContractBillingGenerator.GenerateCandidates(
            contract.ContractId, contract.StartDate, contract.EndDate,
            contract.CurrencyCode, contract.TermsJson, cmd.AsOfDate, existingKeys);

        int count = 0;
        foreach (var candidate in candidates)
        {
            var invoiceId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();

            var invoiceEv = new InvoiceIssued(invoiceId, contract.ContractId, contract.PartyId,
                candidate.CurrencyCode, candidate.Amount, candidate.DueDate,
                candidate.Category, candidate.Reference, null, cmd.EffectiveDate);
            await _store.AppendAsync(new EventEnvelope(
                new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
                nameof(InvoiceIssued), DateTimeOffset.UtcNow, cmd.EffectiveDate,
                actorUserId, deviceId, correlationId, null, 1,
                JsonSerializer.Serialize(invoiceEv, DomainJson.Options)), ct);

            var genEv = new ContractInvoiceGenerated(contract.ContractId, invoiceId, candidate.CycleKey, cmd.EffectiveDate);
            await _store.AppendAsync(new EventEnvelope(
                new EventId(Guid.NewGuid()), BillingStreams.BillingStream,
                nameof(ContractInvoiceGenerated), DateTimeOffset.UtcNow, cmd.EffectiveDate,
                actorUserId, deviceId, correlationId, null, 1,
                JsonSerializer.Serialize(genEv, DomainJson.Options)), ct);

            count++;
        }

        return count;
    }
}
