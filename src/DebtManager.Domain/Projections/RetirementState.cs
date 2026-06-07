using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

public sealed class RetirementProfileRecord
{
    public Guid ProfileId { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public DateOnly RetirementDate { get; set; }
    public Money DesiredMonthlySpending { get; set; }
    public int LifeExpectancyYears { get; set; }
    public string WithdrawalStrategy { get; set; } = "SafeWithdrawalRate";
    public decimal SafeWithdrawalRate { get; set; }
    public DateOnly DefinedDate { get; set; }
}

public sealed class RetirementAssumptionsRecord
{
    public Guid AssumptionsId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal ExpectedAnnualReturnRate { get; set; }
    public decimal ExpectedAnnualInflation { get; set; }
    public decimal ExpectedAnnualSalaryGrowth { get; set; }
    public Money CurrentMonthlySavings { get; set; }
    public string ReportingCurrencyCode { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public DateOnly DefinedDate { get; set; }
}

public sealed class RetirementState
{
    public List<RetirementProfileRecord> Profiles { get; } = new();
    public List<RetirementAssumptionsRecord> AllAssumptions { get; } = new();

    /// <summary>
    /// Gets the latest (most recently defined) profile.
    /// </summary>
    public RetirementProfileRecord? ActiveProfile =>
        Profiles.OrderByDescending(p => p.DefinedDate).FirstOrDefault();

    /// <summary>
    /// Gets the latest active (non-archived) assumptions.
    /// </summary>
    public RetirementAssumptionsRecord? ActiveAssumptions =>
        AllAssumptions.Where(a => !a.IsArchived)
            .OrderByDescending(a => a.DefinedDate)
            .FirstOrDefault();
}
