using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Rules;

namespace DebtManager.Application.UseCases;

/// <summary>
/// DTO for a rule pack assignment to an obligation.
/// </summary>
public sealed record RulePackAssignmentDto(
    Guid ObligationId,
    string PackId,
    string? PackName,
    DateOnly EffectiveDate
);

/// <summary>
/// Query handler to retrieve the current rule pack assignment for an obligation.
/// </summary>
public sealed class GetRulePackAssignmentHandler
{
    private readonly IEventStore _eventStore;
    private readonly IRulePackRepository _repository;

    public GetRulePackAssignmentHandler(IEventStore eventStore, IRulePackRepository repository)
    {
        _eventStore = eventStore;
        _repository = repository;
    }

    /// <summary>
    /// Get the current rule pack assignment for an obligation.
    /// Returns null if no rule pack is assigned.
    /// </summary>
    public async Task<RulePackAssignmentDto?> HandleAsync(Guid obligationId, CancellationToken ct)
    {
        var events = await _eventStore.ReadStreamAsync(
            new StreamId(obligationId),
            upTo: null,
            ct
        );

        // Find the last RulePackAssignedToObligation event
        var lastAssignment = events
            .Where(e => e.EventType == nameof(RulePackAssignedToObligation))
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefault();

        if (lastAssignment == null)
            return null;

        var assignment = JsonSerializer.Deserialize<RulePackAssignedToObligation>(
            lastAssignment.PayloadJson, DomainJson.Options);

        if (assignment == null)
            return null;

        // Try to get the pack name from repository
        string? packName = null;
        try
        {
            var packs = await _repository.GetAllAsync(ct);
            packName = packs.FirstOrDefault(p => p.RulePackId == assignment.RulePackId)?.Name;
        }
        catch
        {
            // Ignore - pack name is optional
        }

        return new RulePackAssignmentDto(
            ObligationId: assignment.ObligationId,
            PackId: assignment.RulePackId,
            PackName: packName,
            EffectiveDate: assignment.EffectiveDate
        );
    }
}
