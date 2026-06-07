namespace DebtManager.Domain.Notifications;

public enum NotificationSeverity { Info, Warning, Error, Critical }

public enum NotificationArea
{
    Cash, Debts, Billing, Budgets, Recurring, Assets,
    Investments, Taxes, Goals, Retirement, Forecast,
    Setup, Sync, DataQuality
}

public enum NotificationStatus { Active, Acknowledged, Dismissed, Snoozed, Expired }

public sealed class NotificationRuleRecord
{
    public Guid RuleId { get; set; }
    public string RuleCode { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    public string ConfigJson { get; set; } = "{}";
    public bool IsEnabled { get; set; } = true;
    public bool IsArchived { get; set; }
}

public sealed record NotificationCandidate(
    Guid NotificationId,
    string RuleCode,
    string Area,
    string Severity,
    string Title,
    string Body,
    DateOnly EffectiveDate,
    DateOnly? DueDate,
    string? RefJson,
    string DedupKey
);

public sealed class NotificationDecision
{
    public Guid NotificationId { get; set; }
    public string Status { get; set; } = "Active";
    public DateOnly? SnoozeUntil { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Note { get; set; }
}

public sealed record NotificationsSummary(
    int TotalActive,
    int CriticalCount,
    int ErrorCount,
    int WarningCount,
    int InfoCount,
    int OverdueCount,
    DateOnly? NextDue
);
