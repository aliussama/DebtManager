using DebtManager.Domain.Events;
using DebtManager.Domain.Projections.Charges;
using DebtManager.Domain.Projections.Installments;
using DebtManager.Domain.ValueObjects;
using DebtManager.Reporting.Models;
using DebtManager.Reporting.Services;

namespace DebtManager.Application.UseCases;

public sealed record GetPortfolioDashboardQuery(
    DateOnly AsOfDate,
    string CurrencyCode = "EGP"
);

/// <summary>
/// Use case: Get portfolio dashboard with all obligations summary.
/// </summary>
public sealed class GetPortfolioDashboardHandler
{
    private readonly IEventStore _eventStore;
    private readonly DashboardGenerator _dashboardGenerator;

    public GetPortfolioDashboardHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
        _dashboardGenerator = new DashboardGenerator();
    }

    public async Task<PortfolioDashboard> HandleAsync(
        GetPortfolioDashboardQuery query,
        CancellationToken ct = default)
    {
        var currency = ResolveCurrency(query.CurrencyCode);

        // Get all events to find obligations
        var allEvents = await _eventStore.ReadAllAsync(
            new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ct
        );

        // Find all obligation created events
        var obligationCreatedEvents = allEvents
            .Where(e => e.EventType == nameof(ObligationCreated))
            .ToList();

        var obligations = new List<ObligationSnapshot>();

        foreach (var envelope in obligationCreatedEvents)
        {
            var created = System.Text.Json.JsonSerializer.Deserialize<ObligationCreated>(
                envelope.PayloadJson, DomainJson.Options);

            if (created == null) continue;

            // Get all events for this obligation
            var obligationEvents = await _eventStore.ReadStreamAsync(
                new StreamId(created.ObligationId),
                upTo: query.AsOfDate,
                ct
            );

            var snapshot = BuildObligationSnapshot(created, obligationEvents, query.AsOfDate, currency);
            obligations.Add(snapshot);
        }

        return _dashboardGenerator.Generate(obligations, query.AsOfDate, currency);
    }

    private static ObligationSnapshot BuildObligationSnapshot(
        ObligationCreated created,
        IReadOnlyList<EventEnvelope> events,
        DateOnly asOfDate,
        Currency currency)
    {
        var isClosed = false;
        DateOnly? closureDate = null;
        var totalPaidAmount = 0m;
        var installments = new List<InstallmentSnapshot>();
        var charges = new List<ComputedCharge>();

        foreach (var envelope in events)
        {
            switch (envelope.EventType)
            {
                case nameof(ObligationClosed):
                    var closed = System.Text.Json.JsonSerializer.Deserialize<ObligationClosed>(
                        envelope.PayloadJson, DomainJson.Options);
                    if (closed != null)
                    {
                        isClosed = true;
                        closureDate = closed.ClosureDate;
                    }
                    break;

                case nameof(PaymentMade):
                    try
                    {
                        var payment = System.Text.Json.JsonSerializer.Deserialize<PaymentMade>(
                            envelope.PayloadJson, DomainJson.Options);
                        if (payment != null)
                        {
                            totalPaidAmount += payment.Amount.Amount;
                        }
                    }
                    catch
                    {
                        // Skip malformed events
                    }
                    break;

                case nameof(PaymentReversed):
                    try
                    {
                        var reversed = System.Text.Json.JsonSerializer.Deserialize<PaymentReversed>(
                            envelope.PayloadJson, DomainJson.Options);
                        if (reversed != null)
                        {
                            totalPaidAmount -= reversed.Amount.Amount;
                        }
                    }
                    catch
                    {
                        // Skip malformed events
                    }
                    break;

                case nameof(ScheduleDefined):
                    try
                    {
                        var schedule = System.Text.Json.JsonSerializer.Deserialize<ScheduleDefined>(
                            envelope.PayloadJson, DomainJson.Options);
                        if (schedule != null)
                        {
                            var expanded = ExpandScheduleToInstallments(schedule, asOfDate, currency);
                            installments.AddRange(expanded);
                        }
                    }
                    catch
                    {
                        // Skip malformed events
                    }
                    break;
            }
        }

        // Ensure paid amount doesn't go negative
        if (totalPaidAmount < 0) totalPaidAmount = 0;

        var totalPaid = new Money(totalPaidAmount, currency);
        var outstandingBalance = created.Principal.Subtract(totalPaid);
        
        // Protect against negative outstanding
        if (outstandingBalance.Amount < 0)
            outstandingBalance = Money.Zero(currency);

        // Update installment statuses based on payments and due dates
        var updatedInstallments = UpdateInstallmentStatuses(installments, totalPaidAmount, asOfDate, currency);

        return new ObligationSnapshot(
            ObligationId: created.ObligationId,
            Name: created.Name,
            ObligationType: created.ObligationType,
            Currency: currency,
            Principal: created.Principal,
            TotalPaid: totalPaid,
            OutstandingBalance: outstandingBalance,
            IsClosed: isClosed,
            ClosureDate: closureDate,
            Installments: updatedInstallments.AsReadOnly(),
            Charges: charges.AsReadOnly()
        );
    }

    private static List<InstallmentSnapshot> ExpandScheduleToInstallments(
        ScheduleDefined schedule,
        DateOnly asOfDate,
        Currency currency)
    {
        var installments = new List<InstallmentSnapshot>();

        try
        {
            if (schedule.ScheduleType == "fixed_dates")
            {
                // Parse fixed dates spec
                var spec = System.Text.Json.JsonSerializer.Deserialize<FixedDatesSpec>(
                    schedule.ScheduleSpecJson, DomainJson.Options);

                if (spec?.Dates != null)
                {
                    foreach (var item in spec.Dates)
                    {
                        installments.Add(new InstallmentSnapshot(
                            InstallmentKey: $"{schedule.ScheduleId}:{item.DueDate:yyyyMMdd}",
                            DueDate: item.DueDate,
                            ExpectedAmount: new Money(item.Amount, currency),
                            PaidAmount: Money.Zero(currency),
                            Status: InstallmentStatus.Upcoming
                        ));
                    }
                }
            }
            else if (schedule.ScheduleType.StartsWith("recurring_"))
            {
                // Parse recurring spec
                var spec = System.Text.Json.JsonSerializer.Deserialize<RecurringSpec>(
                    schedule.ScheduleSpecJson, DomainJson.Options);

                if (spec != null)
                {
                    var startDate = DateOnly.TryParse(spec.StartDate, out var sd) ? sd : asOfDate;
                    var endDate = !string.IsNullOrEmpty(spec.EndDate) && DateOnly.TryParse(spec.EndDate, out var ed)
                        ? ed
                        : (DateOnly?)null;

                    var maxOccurrences = spec.MaxOccurrences ?? 120; // Default to 10 years
                    var pattern = spec.Pattern ?? "monthly";

                    var current = GetFirstOccurrence(startDate, spec.DayOfMonth);
                    var count = 0;

                    while (count < maxOccurrences)
                    {
                        if (endDate.HasValue && current > endDate.Value)
                            break;

                        installments.Add(new InstallmentSnapshot(
                            InstallmentKey: $"{schedule.ScheduleId}:{pattern}:{count:D4}",
                            DueDate: current,
                            ExpectedAmount: new Money(spec.Amount, currency),
                            PaidAmount: Money.Zero(currency),
                            Status: InstallmentStatus.Upcoming
                        ));

                        current = GetNextOccurrence(current, pattern, spec.DayOfMonth);
                        count++;
                    }
                }
            }
        }
        catch
        {
            // If parsing fails, return empty list
        }

        return installments;
    }

    private static DateOnly GetFirstOccurrence(DateOnly startDate, int dayOfMonth)
    {
        var day = Math.Min(dayOfMonth, DateTime.DaysInMonth(startDate.Year, startDate.Month));
        var candidate = new DateOnly(startDate.Year, startDate.Month, day);
        
        if (candidate < startDate)
        {
            candidate = candidate.AddMonths(1);
            day = Math.Min(dayOfMonth, DateTime.DaysInMonth(candidate.Year, candidate.Month));
            candidate = new DateOnly(candidate.Year, candidate.Month, day);
        }
        
        return candidate;
    }

    private static DateOnly GetNextOccurrence(DateOnly current, string pattern, int dayOfMonth)
    {
        var months = pattern switch
        {
            "quarterly" => 3,
            "annual" => 12,
            "semiannual" => 6,
            _ => 1 // monthly default
        };

        var next = current.AddMonths(months);
        var day = Math.Min(dayOfMonth, DateTime.DaysInMonth(next.Year, next.Month));
        return new DateOnly(next.Year, next.Month, day);
    }

    private static List<InstallmentSnapshot> UpdateInstallmentStatuses(
        List<InstallmentSnapshot> installments,
        decimal totalPaid,
        DateOnly asOfDate,
        Currency currency)
    {
        var updated = new List<InstallmentSnapshot>();
        var remainingPayment = totalPaid;

        // Sort by due date to allocate payments to oldest first
        var sorted = installments.OrderBy(i => i.DueDate).ToList();

        foreach (var inst in sorted)
        {
            var expectedAmount = inst.ExpectedAmount.Amount;
            var paidAmount = Math.Min(remainingPayment, expectedAmount);
            remainingPayment -= paidAmount;

            InstallmentStatus status;
            if (paidAmount >= expectedAmount)
            {
                status = InstallmentStatus.Paid;
            }
            else if (paidAmount > 0)
            {
                status = InstallmentStatus.PartiallyPaid;
            }
            else if (inst.DueDate < asOfDate)
            {
                status = InstallmentStatus.Overdue;
            }
            else if (inst.DueDate == asOfDate)
            {
                status = InstallmentStatus.DueToday;
            }
            else
            {
                status = InstallmentStatus.Upcoming;
            }

            updated.Add(inst with
            {
                PaidAmount = new Money(paidAmount, currency),
                Status = status
            });
        }

        return updated;
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

    // Internal DTOs for parsing schedule specs
    private sealed record FixedDatesSpec(
        string CurrencyCode,
        List<FixedDateItem> Dates,
        List<string>? Tags
    );

    private sealed record FixedDateItem(DateOnly DueDate, decimal Amount);

    private sealed record RecurringSpec(
        string? Pattern,
        int DayOfMonth,
        string? StartDate,
        string? EndDate,
        int? MaxOccurrences,
        decimal Amount,
        string? CurrencyCode
    );
}
