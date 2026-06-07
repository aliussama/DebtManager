using DebtManager.Domain.Fx;

namespace DebtManager.Domain.Projections;

public sealed class CurrencySettingsState
{
    public string ReportingCurrencyCode { get; set; } = "EGP";
    public FxValuationPolicy Policy { get; set; } = FxValuationPolicy.NearestBefore;
    public int MaxAgeDays { get; set; } = 14;
    public bool IsConfigured { get; set; }
    public Guid ActiveProfileId { get; set; }
    public Dictionary<Guid, CurrencySettingsProfile> Profiles { get; } = new();
}

public sealed class CurrencySettingsProfile
{
    public Guid ProfileId { get; set; }
    public string ReportingCurrencyCode { get; set; } = "EGP";
    public FxValuationPolicy Policy { get; set; } = FxValuationPolicy.NearestBefore;
    public int MaxAgeDays { get; set; } = 14;
    public bool IsArchived { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid EventId { get; set; }
}
