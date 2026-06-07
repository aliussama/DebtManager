namespace DebtManager.Domain.Projections.Installments;

[Flags]
public enum InstallmentRisk
{
    None = 0,
    NearDue = 1,          // within warning window
    PenaltyLikely = 2,    // overdue beyond grace window (placeholder, grace rules later)
}
