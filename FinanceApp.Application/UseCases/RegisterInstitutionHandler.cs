namespace FinanceApp.Application.UseCases;

using FinanceApp.Domain.Aggregates;
using FinanceApp.Domain.Events;
using FinanceApp.Application.Ports;

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
    private readonly TimeProvider _timeProvider;

    public RegisterInstitutionHandler(IEventStore eventStore, TimeProvider timeProvider)
    {
        _eventStore = eventStore;
        _timeProvider = timeProvider;
    }

    public async Task<RegisterInstitutionResult> HandleAsync(
        RegisterInstitutionCommand command,
        CancellationToken ct = default)
    {
        var institutionId = Guid.NewGuid();
        var institution = FinancialInstitution.Register(
            institutionId,
            command.Name,
            command.Type,
            command.CountryCode,
            command.Metadata,
            _timeProvider.GetUtcNow()
        );

        var streamId = $"institution-{institutionId}";
        await _eventStore.AppendAsync(streamId, institution.UncommittedEvents, ct);
        institution.ClearUncommittedEvents();

        return new RegisterInstitutionResult(institutionId);
    }
}