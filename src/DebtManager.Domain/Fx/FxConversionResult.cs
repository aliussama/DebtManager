namespace DebtManager.Domain.Fx;

public sealed record FxConversionResult(
    bool IsKnown,
    decimal RateUsed,
    string Path,
    string Reason,
    DateOnly RateDateUsed,
    int Hops
)
{
    public static FxConversionResult Unknown(string reason)
        => new(false, 0m, string.Empty, reason, default, 0);

    public static FxConversionResult Identity(string currencyCode)
        => new(true, 1m, currencyCode, string.Empty, default, 0);
}
