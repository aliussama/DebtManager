namespace DebtManager.Domain.Fx;

public enum FxValuationPolicy
{
    Spot,
    NearestBefore,
    NearestAfter,
    Nearest,
    EodNearestBefore,
    InterpolateLinear
}

public sealed record FxPolicyConfig(FxValuationPolicy Policy, int MaxAgeDays)
{
    public static readonly FxPolicyConfig Default = new(FxValuationPolicy.NearestBefore, 14);
}
