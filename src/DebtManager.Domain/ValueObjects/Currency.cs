using System.Text.Json.Serialization;
using DebtManager.Domain.ValueObjects.Json;

namespace DebtManager.Domain.ValueObjects;

[JsonConverter(typeof(CurrencyJsonConverter))]
public sealed record Currency(
    string Code,
    int MinorUnits
)
{
    public static readonly Currency EGP = new("EGP", 2);
    public static readonly Currency USD = new("USD", 2);
    public static readonly Currency EUR = new("EUR", 2);

    public decimal Round(decimal amount)
    {
        return Math.Round(amount, MinorUnits, MidpointRounding.AwayFromZero);
    }
}
