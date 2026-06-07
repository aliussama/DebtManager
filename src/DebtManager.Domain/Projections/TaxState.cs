namespace DebtManager.Domain.Projections;

/// <summary>
/// Projected state of a tax profile.
/// </summary>
public sealed class TaxProfileRecord
{
    public Guid ProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public int TaxYearStartMonth { get; set; } = 1;
    public int TaxYearStartDay { get; set; } = 1;
    public string BaseCurrencyCode { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public DateOnly CreatedDate { get; set; }
}

/// <summary>
/// An active tax rule for pattern matching.
/// </summary>
public sealed class TaxRuleRecord
{
    public Guid RuleId { get; set; }
    public string AppliesTo { get; set; } = string.Empty;
    public string MatchValue { get; set; } = string.Empty;
    public string TaxCategory { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
}

/// <summary>
/// Full tax state derived deterministically from events.
/// </summary>
public sealed class TaxState
{
    public Dictionary<Guid, TaxProfileRecord> Profiles { get; } = new();
    public List<TaxRuleRecord> AllRules { get; } = new();
    public List<TaxRuleRecord> ActiveRules => AllRules.Where(r => !r.IsArchived).ToList();
    public Dictionary<(string SourceType, string SourceId), string> ConfirmedClassifications { get; } = new();
}
