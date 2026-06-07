using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

public sealed record RegisterInstitutionCommand(
    string Name,
    InstitutionType Type,
    string CountryCode,
    InstitutionMetadata? Metadata
);

public sealed record RegisterInstitutionResult(Guid InstitutionId);

/// <summary>
/// Use case: Register a new financial institution.
/// </summary>
public sealed class RegisterInstitutionHandler
{
    private readonly IEventStore _eventStore;

    public RegisterInstitutionHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task<RegisterInstitutionResult> HandleAsync(
        RegisterInstitutionCommand cmd,
        Guid actorUserId,
        Guid deviceId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Name))
            throw new ArgumentException("Institution name is required.", nameof(cmd));

        if (string.IsNullOrWhiteSpace(cmd.CountryCode) || cmd.CountryCode.Length != 2)
            throw new ArgumentException("Country code must be a 2-letter ISO code.", nameof(cmd));

        var institutionId = Guid.NewGuid();
        var effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var @event = new FinancialInstitutionRegistered(
            InstitutionId: institutionId,
            Name: cmd.Name.Trim(),
            Type: cmd.Type,
            CountryCode: cmd.CountryCode.ToUpperInvariant(),
            Metadata: cmd.Metadata,
            EffectiveDate: effectiveDate
        );

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(institutionId),
            nameof(FinancialInstitutionRegistered),
            DateTimeOffset.UtcNow,
            effectiveDate,
            actorUserId,
            deviceId,
            CorrelationId: Guid.NewGuid(),
            CausationEventId: null,
            PayloadSchemaVersion: 1,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(@event, DomainJson.Options)
        );

        await _eventStore.AppendAsync(envelope, ct);

        return new RegisterInstitutionResult(institutionId);
    }
}
