using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;
using DebtManager.Reporting.Models;
using DebtManager.Reporting.Services;

namespace DebtManager.Application.UseCases;

public sealed record GetPaymentProjectionsQuery(
    DateOnly From,
    DateOnly To,
    string CurrencyCode = "EGP"
);

/// <summary>
/// Use case: Get payment projections for cash flow planning.
/// </summary>
public sealed class GetPaymentProjectionsHandler
{
    private readonly IEventStore _eventStore;
    private readonly PaymentProjector _projector;

    public GetPaymentProjectionsHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
        _projector = new PaymentProjector();
    }

    public async Task<IReadOnlyList<PaymentProjection>> HandleAsync(
        GetPaymentProjectionsQuery query,
        CancellationToken ct = default)
    {
        var currency = ResolveCurrency(query.CurrencyCode);
        var asOfDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var obligations = await LoadObligationSnapshotsAsync(query.To, currency, ct);

        return _projector.ProjectPayments(obligations, query.From, query.To, asOfDate);
    }

    private async Task<IReadOnlyList<ObligationSnapshot>> LoadObligationSnapshotsAsync(
        DateOnly upTo,
        Currency currency,
        CancellationToken ct)
    {
        var allEvents = await _eventStore.ReadAllAsync(
            new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ct
        );

        var obligationCreatedEvents = allEvents
            .Where(e => e.EventType == nameof(ObligationCreated))
            .ToList();

        var snapshots = new List<ObligationSnapshot>();

        foreach (var envelope in obligationCreatedEvents)
        {
            var created = System.Text.Json.JsonSerializer.Deserialize<ObligationCreated>(
                envelope.PayloadJson, DomainJson.Options);

            if (created == null) continue;

            var obligationEvents = await _eventStore.ReadStreamAsync(
                new StreamId(created.ObligationId),
                upTo: upTo,
                ct
            );

            var snapshot = BuildSnapshot(created, obligationEvents, upTo, currency);
            snapshots.Add(snapshot);
        }

        return snapshots.AsReadOnly();
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
            Charges: Array.Empty<Domain.Projections.Charges.ComputedCharge>()
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
