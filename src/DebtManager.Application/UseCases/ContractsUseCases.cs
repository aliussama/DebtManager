using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Commands ---

public sealed record CreateContractCommand(
    Guid? ContractId, Guid PartyId, string ContractType, string Title,
    DateOnly StartDate, DateOnly? EndDate, string CurrencyCode, string TermsJson,
    DateOnly EffectiveDate);

public sealed record ModifyContractCommand(
    Guid ContractId, string Title, DateOnly? EndDate, string TermsJson,
    DateOnly EffectiveDate);

public sealed record ArchiveContractCommand(Guid ContractId, string Reason, DateOnly EffectiveDate);

// --- DTOs ---

public sealed record ContractListItemDto(
    Guid ContractId, Guid PartyId, string PartyName,
    string ContractType, string Title,
    DateOnly StartDate, DateOnly? EndDate,
    string CurrencyCode, string TermsJson, bool IsArchived);

public sealed record ContractDetailDto(
    ContractListItemDto Contract,
    IReadOnlyList<BillListItemDto> Bills,
    IReadOnlyList<InvoiceListItemDto> Invoices);

// --- Handlers ---

public sealed class CreateContractHandler
{
    private readonly IEventStore _store;
    public CreateContractHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(CreateContractCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var id = cmd.ContractId ?? Guid.NewGuid();
        var ev = new ContractCreated(id, cmd.PartyId, cmd.ContractType, cmd.Title,
            cmd.StartDate, cmd.EndDate, cmd.CurrencyCode, cmd.TermsJson, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(id),
            nameof(ContractCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
        return id;
    }
}

public sealed class ModifyContractHandler
{
    private readonly IEventStore _store;
    public ModifyContractHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ModifyContractCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new ContractModified(cmd.ContractId, cmd.Title, cmd.EndDate, cmd.TermsJson, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.ContractId),
            nameof(ContractModified), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class ArchiveContractHandler
{
    private readonly IEventStore _store;
    public ArchiveContractHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveContractCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new ContractArchived(cmd.ContractId, cmd.Reason, cmd.EffectiveDate);
        await _store.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.ContractId),
            nameof(ContractArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), ct);
    }
}

public sealed class GetContractsListHandler
{
    private readonly IEventStore _store;
    public GetContractsListHandler(IEventStore store) => _store = store;

    public async Task<IReadOnlyList<ContractListItemDto>> HandleAsync(bool includeArchived, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var contractState = ContractsProjector.Project(all);
        var partyState = PartiesProjector.Project(all);

        return contractState.Contracts.Values
            .Where(c => includeArchived || !c.IsArchived)
            .Select(c =>
            {
                var partyName = partyState.Parties.TryGetValue(c.PartyId, out var p) ? p.Name : c.PartyId.ToString();
                return new ContractListItemDto(c.ContractId, c.PartyId, partyName,
                    c.ContractType, c.Title, c.StartDate, c.EndDate,
                    c.CurrencyCode, c.TermsJson, c.IsArchived);
            })
            .ToList();
    }
}

public sealed class GetContractDetailHandler
{
    private readonly IEventStore _store;
    public GetContractDetailHandler(IEventStore store) => _store = store;

    public async Task<ContractDetailDto?> HandleAsync(Guid contractId, CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var contractState = ContractsProjector.Project(all);
        var partyState = PartiesProjector.Project(all);
        var billingState = BillingProjector.Project(all);

        if (!contractState.Contracts.TryGetValue(contractId, out var c))
            return null;

        var partyName = partyState.Parties.TryGetValue(c.PartyId, out var p) ? p.Name : c.PartyId.ToString();
        var contract = new ContractListItemDto(c.ContractId, c.PartyId, partyName,
            c.ContractType, c.Title, c.StartDate, c.EndDate,
            c.CurrencyCode, c.TermsJson, c.IsArchived);

        var bills = billingState.BillsByContract(contractId)
            .Select(b => new BillListItemDto(b.BillId, b.ContractId, b.PartyId, "", b.CurrencyCode,
                b.Amount, b.DueDate, b.Category, b.Reference, b.Status, b.Outstanding, b.TotalPaid))
            .ToList();

        var invoices = billingState.InvoicesByContract(contractId)
            .Select(i => new InvoiceListItemDto(i.InvoiceId, i.ContractId, i.PartyId, "", i.CurrencyCode,
                i.Amount, i.DueDate, i.Category, i.Reference, i.Status, i.Outstanding, i.TotalPaid))
            .ToList();

        return new ContractDetailDto(contract, bills, invoices);
    }
}
