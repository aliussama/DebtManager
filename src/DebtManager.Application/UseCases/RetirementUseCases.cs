using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Planning;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Commands ---

public sealed record DefineRetirementProfileCommand(
    Guid? ProfileId, string ProfileName, DateOnly RetirementDate,
    decimal DesiredMonthlySpending, string CurrencyCode,
    int LifeExpectancyYears, string WithdrawalStrategy,
    decimal SafeWithdrawalRate, DateOnly EffectiveDate);

public sealed record SetRetirementAssumptionsCommand(
    Guid? AssumptionsId, string Name,
    decimal ExpectedAnnualReturnRate, decimal ExpectedAnnualInflation,
    decimal ExpectedAnnualSalaryGrowth,
    decimal CurrentMonthlySavings, string SavingsCurrencyCode,
    string ReportingCurrencyCode, DateOnly EffectiveDate);

public sealed record ArchiveRetirementAssumptionsCommand(
    Guid AssumptionsId, DateOnly EffectiveDate, string Reason);

// --- DTOs ---

public sealed record RetirementPlanReportDto(
    bool HasProfile,
    bool HasAssumptions,
    string? ErrorMessage,
    RetirementPlanResult? Plan);

// --- Handlers ---

public sealed class DefineRetirementProfileHandler
{
    private readonly IEventStore _store;
    public DefineRetirementProfileHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(DefineRetirementProfileCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var id = cmd.ProfileId ?? Guid.NewGuid();
        var money = new Money(cmd.DesiredMonthlySpending, new Currency(cmd.CurrencyCode, 2));
        var ev = new RetirementProfileDefined(id, cmd.ProfileName, cmd.RetirementDate,
            money, cmd.LifeExpectancyYears, cmd.WithdrawalStrategy,
            cmd.SafeWithdrawalRate, cmd.EffectiveDate);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(id),
            nameof(RetirementProfileDefined), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return id;
    }
}

public sealed class SetRetirementAssumptionsHandler
{
    private readonly IEventStore _store;
    public SetRetirementAssumptionsHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(SetRetirementAssumptionsCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var id = cmd.AssumptionsId ?? Guid.NewGuid();
        var money = new Money(cmd.CurrentMonthlySavings, new Currency(cmd.SavingsCurrencyCode, 2));
        var ev = new RetirementAssumptionsSet(id, cmd.Name,
            cmd.ExpectedAnnualReturnRate, cmd.ExpectedAnnualInflation,
            cmd.ExpectedAnnualSalaryGrowth, money,
            cmd.ReportingCurrencyCode, cmd.EffectiveDate);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(id),
            nameof(RetirementAssumptionsSet), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return id;
    }
}

public sealed class ArchiveRetirementAssumptionsHandler
{
    private readonly IEventStore _store;
    public ArchiveRetirementAssumptionsHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveRetirementAssumptionsCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new RetirementAssumptionsArchived(cmd.AssumptionsId, cmd.EffectiveDate, cmd.Reason);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.AssumptionsId),
            nameof(RetirementAssumptionsArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class GetRetirementPlanReportHandler
{
    private readonly IEventStore _store;
    private readonly GetNetWorthReportHandler _netWorthHandler;

    public GetRetirementPlanReportHandler(IEventStore store, GetNetWorthReportHandler netWorthHandler)
    {
        _store = store;
        _netWorthHandler = netWorthHandler;
    }

    public async Task<RetirementPlanReportDto> HandleAsync(DateOnly asOfDate, CancellationToken ct)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var retirementState = RetirementProjector.Project(envelopes, asOfDate);

        var profile = retirementState.ActiveProfile;
        var assumptions = retirementState.ActiveAssumptions;

        if (profile == null)
            return new RetirementPlanReportDto(false, assumptions != null,
                "No retirement profile defined. Please create one first.", null);

        if (assumptions == null)
            return new RetirementPlanReportDto(true, false,
                "No active retirement assumptions. Please define assumptions first.", null);

        var reportCcy = assumptions.ReportingCurrencyCode;
        var nwReport = await _netWorthHandler.HandleAsync(
            new GetNetWorthReportQuery(asOfDate, reportCcy), ct);

        var plan = RetirementPlanner.Compute(
            profile, assumptions,
            nwReport.KnownNetWorth, nwReport.UnknownValueCount,
            asOfDate);

        return new RetirementPlanReportDto(true, true, null, plan);
    }
}
