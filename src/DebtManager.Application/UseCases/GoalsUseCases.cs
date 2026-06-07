using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Commands ---

public sealed record CreateFinancialGoalCommand(
    Guid? GoalId, string Name, string GoalType,
    decimal TargetAmount, string CurrencyCode,
    DateOnly TargetDate, string? Notes, string[] Tags,
    DateOnly EffectiveDate);

public sealed record ModifyFinancialGoalCommand(
    Guid GoalId, string Name, string GoalType,
    decimal TargetAmount, string CurrencyCode,
    DateOnly TargetDate, string? Notes, string[] Tags,
    DateOnly EffectiveDate);

public sealed record ArchiveFinancialGoalCommand(Guid GoalId, DateOnly EffectiveDate, string Reason);

public sealed record RecordGoalContributionCommand(
    Guid GoalId, Guid? ContributionId, Guid AccountId,
    decimal Amount, string CurrencyCode,
    DateOnly EffectiveDate, string Reference);

public sealed record ReverseGoalContributionCommand(
    Guid GoalId, Guid ContributionId,
    DateOnly EffectiveDate, string Reason);

public sealed record GoalsDashboardQuery(
    DateOnly? AsOfDate = null,
    string? GoalTypeFilter = null,
    bool IncludeArchived = false);

// --- DTOs ---

public sealed record GoalSummaryDto(
    Guid GoalId, string Name, string GoalType,
    decimal TargetAmount, string CurrencyCode,
    DateOnly TargetDate, decimal Contributed,
    decimal Remaining, decimal ProgressPercent,
    DateOnly? EstimatedCompletionDate,
    decimal AvgMonthlyContribution,
    string Status, string? Notes, string[] Tags);

public sealed record GoalContributionDto(
    Guid ContributionId, Guid GoalId, string GoalName,
    Guid AccountId, string AccountName,
    decimal Amount, string CurrencyCode,
    DateOnly EffectiveDate, string Reference,
    string Status);

public sealed record GoalsDashboardDto(
    IReadOnlyList<GoalSummaryDto> Goals,
    IReadOnlyList<GoalContributionDto> RecentContributions,
    decimal TotalTargetAmount, decimal TotalContributed,
    decimal OverallProgressPercent, int ActiveGoalCount);

// --- Handlers ---

public sealed class CreateFinancialGoalHandler
{
    private readonly IEventStore _store;
    public CreateFinancialGoalHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(CreateFinancialGoalCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var id = cmd.GoalId ?? Guid.NewGuid();
        var money = new Money(cmd.TargetAmount, new Currency(cmd.CurrencyCode, 2));
        var ev = new FinancialGoalCreated(id, cmd.Name, cmd.GoalType, money,
            cmd.TargetDate, cmd.Notes, cmd.Tags ?? [], cmd.EffectiveDate);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(id),
            nameof(FinancialGoalCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return id;
    }
}

public sealed class ModifyFinancialGoalHandler
{
    private readonly IEventStore _store;
    public ModifyFinancialGoalHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ModifyFinancialGoalCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var money = new Money(cmd.TargetAmount, new Currency(cmd.CurrencyCode, 2));
        var ev = new FinancialGoalModified(cmd.GoalId, cmd.Name, cmd.GoalType, money,
            cmd.TargetDate, cmd.Notes, cmd.Tags ?? [], cmd.EffectiveDate);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.GoalId),
            nameof(FinancialGoalModified), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class ArchiveFinancialGoalHandler
{
    private readonly IEventStore _store;
    public ArchiveFinancialGoalHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ArchiveFinancialGoalCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new FinancialGoalArchived(cmd.GoalId, cmd.EffectiveDate, cmd.Reason);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.GoalId),
            nameof(FinancialGoalArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class RecordGoalContributionHandler
{
    private readonly IEventStore _store;
    public RecordGoalContributionHandler(IEventStore store) => _store = store;

    public async Task<Guid> HandleAsync(RecordGoalContributionCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var id = cmd.ContributionId ?? Guid.NewGuid();
        var money = new Money(cmd.Amount, new Currency(cmd.CurrencyCode, 2));
        var ev = new GoalContributionRecorded(cmd.GoalId, id, cmd.AccountId, money,
            cmd.EffectiveDate, cmd.Reference);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.GoalId),
            nameof(GoalContributionRecorded), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
        return id;
    }
}

public sealed class ReverseGoalContributionHandler
{
    private readonly IEventStore _store;
    public ReverseGoalContributionHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(ReverseGoalContributionCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new GoalContributionReversed(cmd.GoalId, cmd.ContributionId, cmd.EffectiveDate, cmd.Reason);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(cmd.GoalId),
            nameof(GoalContributionReversed), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class GetGoalsDashboardHandler
{
    private readonly IEventStore _store;
    public GetGoalsDashboardHandler(IEventStore store) => _store = store;

    public async Task<GoalsDashboardDto> HandleAsync(GoalsDashboardQuery query, CancellationToken ct)
    {
        var asOf = query.AsOfDate ?? DateOnly.FromDateTime(DateTime.Today);
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);

        var goalsState = GoalsProjector.Project(envelopes, asOf);
        var cashState = CashLedgerProjector.Project(envelopes, asOf);

        var goalDtos = new List<GoalSummaryDto>();
        foreach (var goal in goalsState.Goals.Values)
        {
            if (!query.IncludeArchived && goal.IsArchived) continue;
            if (!string.IsNullOrEmpty(query.GoalTypeFilter) &&
                !string.Equals(goal.GoalType, query.GoalTypeFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            var contributed = goalsState.TotalContributed(goal.GoalId);
            var remaining = goalsState.RemainingAmount(goal.GoalId);
            var progress = goalsState.ProgressPercent(goal.GoalId);
            var eta = goalsState.EstimatedCompletionDate(goal.GoalId, asOf);
            var avgMonthly = goalsState.AvgMonthlyContribution(goal.GoalId, asOf);

            var status = goal.IsArchived ? "Archived"
                : progress >= 100m ? "Completed"
                : goal.TargetDate < asOf ? "Overdue"
                : "Active";

            goalDtos.Add(new GoalSummaryDto(
                goal.GoalId, goal.Name, goal.GoalType,
                goal.TargetAmount.Amount, goal.TargetAmount.Currency.Code,
                goal.TargetDate, contributed, remaining, progress, eta, avgMonthly,
                status, goal.Notes, goal.Tags));
        }

        var contributionDtos = new List<GoalContributionDto>();
        foreach (var (goalId, contribs) in goalsState.ContributionsByGoal)
        {
            if (!goalsState.Goals.TryGetValue(goalId, out var goal)) continue;
            foreach (var c in contribs.OrderByDescending(x => x.EffectiveDate).Take(50))
            {
                var accountName = cashState.Accounts.TryGetValue(c.AccountId, out var acct)
                    ? acct.Name : c.AccountId.ToString();
                contributionDtos.Add(new GoalContributionDto(
                    c.ContributionId, c.GoalId, goal.Name,
                    c.AccountId, accountName,
                    c.Amount.Amount, c.Amount.Currency.Code,
                    c.EffectiveDate, c.Reference,
                    c.IsReversed ? "Reversed" : "Active"));
            }
        }

        var activeGoals = goalDtos.Where(g => g.Status is "Active" or "Overdue" or "Completed").ToList();
        var totalTarget = activeGoals.Sum(g => g.TargetAmount);
        var totalContributed = activeGoals.Sum(g => g.Contributed);
        var overallProgress = totalTarget > 0
            ? Math.Round(totalContributed / totalTarget * 100m, 2, MidpointRounding.AwayFromZero) : 0m;

        return new GoalsDashboardDto(
            goalDtos, contributionDtos.OrderByDescending(c => c.EffectiveDate).ToList(),
            totalTarget, totalContributed, overallProgress,
            activeGoals.Count(g => g.Status is "Active" or "Overdue"));
    }
}
