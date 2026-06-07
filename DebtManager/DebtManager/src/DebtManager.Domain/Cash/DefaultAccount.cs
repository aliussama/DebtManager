namespace DebtManager.Domain.Cash;

public static class DefaultAccount
{
    // deterministic single account for v1
    public static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
}
