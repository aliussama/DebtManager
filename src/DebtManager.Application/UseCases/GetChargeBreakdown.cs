using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;
using DebtManager.Reporting.Models;
using DebtManager.Reporting.Services;

namespace DebtManager.Application.UseCases;

public sealed record GetChargeBreakdownQuery(
    Guid ObligationId,
    DateOnly AsOfDate
);

/// <summary>
/// Use case: Get detailed charge breakdown for an obligation.
/// </summary>
public sealed class GetChargeBreakdownHandler
{
    private readonly IEventStore _eventStore;
    private readonly ChargeReportGenerator _generator;

    public GetChargeBreakdownHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
        _generator = new ChargeReportGenerator();
    }

    public async Task<ChargeBreakdownReport> HandleAsync(
        GetChargeBreakdownQuery query,
        CancellationToken ct = default)
    {
        var streamId = new StreamId(query.ObligationId);
        var events = await _eventStore.ReadStreamAsync(streamId, upTo: query.AsOfDate, ct);

        if (!events.Any())
            throw new InvalidOperationException($"Obligation {query.ObligationId} not found.");

        var createdEnvelope = events.First(e => e.EventType == nameof(ObligationCreated));
        var created = System.Text.Json.JsonSerializer.Deserialize<ObligationCreated>(
            createdEnvelope.PayloadJson, DomainJson.Options);

        if (created == null)
            throw new InvalidOperationException($"Could not deserialize obligation {query.ObligationId}.");

        var currency = ResolveCurrency(created.CurrencyCode);
        var snapshot = BuildSnapshot(created, events, query.AsOfDate, currency);

        return _generator.Generate(snapshot, query.AsOfDate);
    }

    private static ObligationSnapshot BuildSnapshot(
        ObligationCreated created,
        IReadOnlyList<EventEnvelope> events,
        DateOnly asOfDate,
        Currency currency)
    {
        var isClosed = events.Any(e => e.EventType == nameof(ObligationClosed));
        var totalPaid = Money.Zero(currency);
        var outstandingBalance = created.Principal.Subtract(totalPaid);

        // TODO: Load charges from events/rules
        var charges = new List<Domain.Projections.Charges.ComputedCharge>();

        return new ObligationSnapshot(
            ObligationId: created.ObligationId,
            Name: created.Name,
            ObligationType: created.ObligationType,
            Currency: currency,
            Principal: created.Principal,
            TotalPaid: totalPaid,
            OutstandingBalance: outstandingBalance,
            IsClosed: isClosed,
            ClosureDate: null,
            Installments: Array.Empty<InstallmentSnapshot>(),
            Charges: charges.AsReadOnly()
        );
    }

    private static Currency ResolveCurrency(string code)
    {
        return code.ToUpperInvariant() switch
        {
            "USD" => Currency.USD,
            "EUR" => Currency.EUR,
            _ => Currency.EGP
        };
    }
}
