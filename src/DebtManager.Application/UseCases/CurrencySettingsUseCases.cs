using System.Text.Json;
using System.Text.RegularExpressions;
using DebtManager.Domain.Events;
using DebtManager.Domain.Fx;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

// Dedicated stream for currency settings
public static class CurrencySettingsStream
{
    public static readonly StreamId Id = new(Guid.Parse("00000000-0000-0000-0000-C0FFEE000001"));
}

// --- DTOs ---

public sealed record CurrencySettingsDto(
    string ReportingCurrencyCode,
    FxValuationPolicy Policy,
    int MaxAgeDays,
    bool IsConfigured
);

// --- Commands ---

public sealed record SetReportingCurrencyCommand(string ReportingCurrencyCode);
public sealed record SetFxPolicyCommand(FxValuationPolicy Policy, int MaxAgeDays);

// --- Handlers ---

public sealed class GetCurrencySettingsHandler
{
    private readonly IEventStore _store;
    public GetCurrencySettingsHandler(IEventStore store) => _store = store;

    public async Task<CurrencySettingsDto> HandleAsync(CancellationToken ct = default)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = CurrencySettingsProjector.Project(envelopes);
        return new CurrencySettingsDto(
            state.ReportingCurrencyCode,
            state.Policy,
            state.MaxAgeDays,
            state.IsConfigured);
    }
}

public sealed class SetReportingCurrencyHandler
{
    private readonly IEventStore _store;
    public SetReportingCurrencyHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(SetReportingCurrencyCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var code = cmd.ReportingCurrencyCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!Regex.IsMatch(code, @"^[A-Z]{3}$"))
            throw new InvalidOperationException("Currency code must be exactly 3 upper-case letters.");

        // Get or create profile id
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = CurrencySettingsProjector.Project(envelopes);
        var profileId = state.IsConfigured ? state.ActiveProfileId : Guid.NewGuid();

        var ev = new ReportingCurrencySet(profileId, code, DateOnly.FromDateTime(DateTime.Today));
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), CurrencySettingsStream.Id,
            nameof(ReportingCurrencySet), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class SetFxPolicyHandler
{
    private readonly IEventStore _store;
    public SetFxPolicyHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(SetFxPolicyCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        if (cmd.MaxAgeDays < 0 || cmd.MaxAgeDays > 3650)
            throw new InvalidOperationException("MaxAgeDays must be between 0 and 3650.");

        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var state = CurrencySettingsProjector.Project(envelopes);
        var profileId = state.IsConfigured ? state.ActiveProfileId : Guid.NewGuid();

        var ev = new FxPolicySet(profileId, cmd.Policy, cmd.MaxAgeDays, DateOnly.FromDateTime(DateTime.Today));
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), CurrencySettingsStream.Id,
            nameof(FxPolicySet), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}

public sealed class ArchiveCurrencySettingsHandler
{
    private readonly IEventStore _store;
    public ArchiveCurrencySettingsHandler(IEventStore store) => _store = store;

    public async Task HandleAsync(Guid profileId, string reason, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new CurrencySettingsArchived(profileId, DateOnly.FromDateTime(DateTime.Today), reason);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), CurrencySettingsStream.Id,
            nameof(CurrencySettingsArchived), DateTimeOffset.UtcNow, ev.EffectiveDate,
            actorUserId, deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _store.AppendAsync(env, ct);
    }
}
