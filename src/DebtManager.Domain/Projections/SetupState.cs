namespace DebtManager.Domain.Projections;

public sealed class SetupState
{
    public bool IsInitialSetupCompleted { get; set; }
    public bool IsDemoModeActive { get; set; }
    public DateOnly? CompletedOn { get; set; }
    public Guid? SetupId { get; set; }
    public Guid? DemoSeedId { get; set; }
    public string ReportingCurrencyCode { get; set; } = string.Empty;
    public int FiscalYearStartMonth { get; set; }
    public bool CreatedDefaultAccounts { get; set; }
    public bool CreatedDefaultCategories { get; set; }
    public bool SeededDemoData { get; set; }
}
