using DebtManager.Reporting.Models;

namespace DebtManager.Application.UseCases;

/// <summary>
/// DTO for an obligation list item with computed fields.
/// </summary>
public sealed record ObligationListItemDto(
    Guid ObligationId,
    string Name,
    string ObligationType,
    decimal Principal,
    decimal TotalPaid,
    decimal Outstanding,
    int OverdueCount,
    DateOnly? NextDueDate,
    string HealthStatus,
    bool IsClosed,
    string CurrencyCode
)
{
    public string NextDueDateDisplay => NextDueDate?.ToString("MMM dd, yyyy") ?? "—";
    public string Status => IsClosed ? "Closed" : (Outstanding <= 0 ? "Paid Off" : "Active");
}

/// <summary>
/// Query handler to retrieve all obligations with computed summary data.
/// Reuses GetPortfolioDashboardHandler internally to avoid duplicating logic.
/// </summary>
public sealed class GetObligationsListHandler
{
    private readonly GetPortfolioDashboardHandler _dashboardHandler;

    public GetObligationsListHandler(GetPortfolioDashboardHandler dashboardHandler)
    {
        _dashboardHandler = dashboardHandler;
    }

    public async Task<IReadOnlyList<ObligationListItemDto>> HandleAsync(
        DateOnly asOfDate,
        string currencyCode = "EGP",
        CancellationToken ct = default)
    {
        var query = new GetPortfolioDashboardQuery(asOfDate, currencyCode);
        var dashboard = await _dashboardHandler.HandleAsync(query, ct);

        return dashboard.Obligations
            .Select(o => new ObligationListItemDto(
                ObligationId: o.ObligationId,
                Name: o.Name,
                ObligationType: o.ObligationType,
                Principal: o.Principal.Amount,
                TotalPaid: o.TotalPaid.Amount,
                Outstanding: o.OutstandingBalance.Amount,
                OverdueCount: o.OverdueInstallments,
                NextDueDate: o.NextDueDate,
                HealthStatus: MapHealthStatus(o.HealthStatus),
                IsClosed: o.IsClosed,
                CurrencyCode: o.Principal.Currency.Code
            ))
            .ToList();
    }

    private static string MapHealthStatus(ObligationHealthStatus status)
    {
        return status switch
        {
            ObligationHealthStatus.Healthy => "Healthy",
            ObligationHealthStatus.AtRisk => "AtRisk",
            ObligationHealthStatus.Delinquent => "Delinquent",
            ObligationHealthStatus.Critical => "Critical",
            _ => "Unknown"
        };
    }
}
