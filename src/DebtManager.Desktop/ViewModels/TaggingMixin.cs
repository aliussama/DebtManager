using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using System.Collections.ObjectModel;

namespace DebtManager.Desktop.ViewModels;

/// <summary>
/// Shared helper for tag operations across ViewModels.
/// No business logic — only presentation helpers that delegate to handlers.
/// </summary>
public sealed class TaggingMixin
{
    private readonly UpdateEntityTagsHandler? _updateHandler;
    private readonly GetTagSuggestionsHandler? _suggestionsHandler;
    private readonly GetEntitiesByTagHandler? _entitiesByTagHandler;
    private readonly IEventStore? _eventStore;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;

    public TaggingMixin(
        UpdateEntityTagsHandler? updateHandler,
        GetTagSuggestionsHandler? suggestionsHandler,
        GetEntitiesByTagHandler? entitiesByTagHandler,
        IEventStore? eventStore,
        Guid actorUserId, Guid deviceId,
        IToastService? toastService)
    {
        _updateHandler = updateHandler;
        _suggestionsHandler = suggestionsHandler;
        _entitiesByTagHandler = entitiesByTagHandler;
        _eventStore = eventStore;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
    }

    public async Task LoadSuggestionsAsync(ObservableCollection<string> target)
    {
        if (_suggestionsHandler == null) return;
        try
        {
            var suggestions = await _suggestionsHandler.HandleAsync(CancellationToken.None);
            target.Clear();
            target.Add(string.Empty); // "All" option for filter
            foreach (var s in suggestions)
                target.Add(s.Tag);
        }
        catch { /* non-critical */ }
    }

    public async Task LoadEntityTagsAsync(Guid entityId, string entityType, ObservableCollection<string> target)
    {
        target.Clear();
        if (_eventStore == null) return;
        try
        {
            var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
            var state = TagProjector.Project(all);
            if (state.EntityTags.TryGetValue((entityId, entityType), out var tags))
            {
                foreach (var tag in tags)
                    target.Add(tag);
            }
        }
        catch { /* non-critical */ }
    }

    public async Task SaveTagsAsync(Guid entityId, string entityType, IEnumerable<string> tags, ObservableCollection<string> suggestions)
    {
        if (_updateHandler == null) return;
        try
        {
            await _updateHandler.HandleAsync(
                new UpdateEntityTagsCommand(entityId, entityType, tags.ToList(), DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Tags updated");
            await LoadSuggestionsAsync(suggestions);
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to update tags", ex);
        }
    }

    public void AddTag(string? text, ObservableCollection<string> tags)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;
        if (trimmed.Length > 50) return;
        if (tags.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) return;
        if (tags.Count >= 20) return;
        tags.Add(trimmed);
    }

    public void RemoveTag(string? tag, ObservableCollection<string> tags)
    {
        if (tag != null) tags.Remove(tag);
    }

    public async Task<HashSet<Guid>> GetEntityIdsByTagAsync(string tag, string entityType)
    {
        if (_entitiesByTagHandler == null || string.IsNullOrWhiteSpace(tag))
            return new HashSet<Guid>();
        try
        {
            var results = await _entitiesByTagHandler.HandleAsync(tag, CancellationToken.None);
            return results
                .Where(r => r.EntityType == entityType)
                .Select(r => r.EntityId)
                .ToHashSet();
        }
        catch { return new HashSet<Guid>(); }
    }
}
