using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// --- Commands ---

public sealed record UpdateEntityTagsCommand(
    Guid EntityId,
    string EntityType,
    IReadOnlyList<string> Tags,
    DateOnly EffectiveDate
);

// --- Query Results ---

public sealed record TagSuggestionItem(string Tag, int UsageCount);

public sealed record TaggedEntityItem(Guid EntityId, string EntityType);

// --- Handlers ---

public sealed class UpdateEntityTagsHandler
{
    private readonly IEventStore _store;

    public UpdateEntityTagsHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(
        UpdateEntityTagsCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        // Validate tag count
        if (cmd.Tags.Count > 20)
            throw new InvalidOperationException("Maximum 20 tags per entity.");

        // Validate + normalize tags
        var normalized = new List<string>(cmd.Tags.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in cmd.Tags)
        {
            var trimmed = tag.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            if (trimmed.Length > 50)
                throw new InvalidOperationException($"Tag '{trimmed}' exceeds 50 character limit.");

            if (!seen.Add(trimmed))
                continue; // case-insensitive duplicate

            normalized.Add(trimmed);
        }

        if (string.IsNullOrWhiteSpace(cmd.EntityType))
            throw new InvalidOperationException("EntityType is required.");

        // Validate entity exists based on EntityType
        var allEnvelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        ValidateEntityExists(cmd.EntityId, cmd.EntityType, allEnvelopes);

        var ev = new EntityTagsReplaced(
            cmd.EntityId,
            cmd.EntityType,
            normalized,
            cmd.EffectiveDate);

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(cmd.EntityId),
            nameof(EntityTagsReplaced),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            actorUserId,
            deviceId,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options));

        await _store.AppendAsync(envelope, ct);
    }

    private static void ValidateEntityExists(Guid entityId, string entityType, IReadOnlyList<EventEnvelope> envelopes)
    {
        bool exists = entityType switch
        {
            "Account" => CashLedgerProjector.Project(envelopes).Accounts.ContainsKey(entityId),
            "Obligation" => envelopes.Any(e => e.EventType == nameof(ObligationCreated) &&
                JsonSerializer.Deserialize<ObligationCreated>(e.PayloadJson, DomainJson.Options)?.ObligationId == entityId),
            "Asset" => AssetsProjector.Project(envelopes).Assets.ContainsKey(entityId),
            "Party" => PartiesProjector.Project(envelopes).Parties.ContainsKey(entityId),
            "Contract" => ContractsProjector.Project(envelopes).Contracts.ContainsKey(entityId),
            "Bill" => BillingProjector.Project(envelopes).Bills.ContainsKey(entityId),
            "Invoice" => BillingProjector.Project(envelopes).Invoices.ContainsKey(entityId),
            "Goal" => GoalsProjector.Project(envelopes).Goals.ContainsKey(entityId),
            "Recurring" => RecurringProjector.Project(envelopes).Items.ContainsKey(entityId),
            _ => true // Allow unknown entity types for extensibility
        };

        if (!exists)
            throw new InvalidOperationException($"{entityType} with ID {entityId} not found.");
    }
}

public sealed class GetTagSuggestionsHandler
{
    private readonly IEventStore _store;

    public GetTagSuggestionsHandler(IEventStore store) => _store = store;

    public async Task<IReadOnlyList<TagSuggestionItem>> HandleAsync(CancellationToken ct)
    {
        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = TagProjector.Project(all);

        return state.TagUsageCounts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new TagSuggestionItem(kv.Key, kv.Value))
            .ToList();
    }
}

public sealed class GetEntitiesByTagHandler
{
    private readonly IEventStore _store;

    public GetEntitiesByTagHandler(IEventStore store) => _store = store;

    public async Task<IReadOnlyList<TaggedEntityItem>> HandleAsync(string tag, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return Array.Empty<TaggedEntityItem>();

        var all = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = TagProjector.Project(all);

        var trimmedTag = tag.Trim();

        return state.EntityTags
            .Where(kv => kv.Value.Contains(trimmedTag, StringComparer.OrdinalIgnoreCase))
            .Select(kv => new TaggedEntityItem(kv.Key.EntityId, kv.Key.EntityType))
            .OrderBy(e => e.EntityType)
            .ThenBy(e => e.EntityId)
            .ToList();
    }
}
