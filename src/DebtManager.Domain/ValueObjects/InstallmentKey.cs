namespace DebtManager.Domain.ValueObjects;

public readonly record struct InstallmentKey(Guid Value)
{
    public static InstallmentKey New() => new(Guid.NewGuid());
}
