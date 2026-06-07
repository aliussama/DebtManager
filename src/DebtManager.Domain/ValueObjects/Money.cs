using System.Text.Json.Serialization;
using DebtManager.Domain.ValueObjects.Json;

namespace DebtManager.Domain.ValueObjects;

[JsonConverter(typeof(MoneyJsonConverter))]
public readonly record struct Money(
    decimal Amount,
    Currency Currency
)
{
    public static Money Zero(Currency currency) => new(0m, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Currency.Round(Amount + other.Amount), Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Currency.Round(Amount - other.Amount), Currency);
    }

    public Money Multiply(decimal factor)
    {
        return new Money(Currency.Round(Amount * factor), Currency);
    }

    private void EnsureSameCurrency(Money other)
    {
        if (Currency is null || other.Currency is null)
            throw new InvalidOperationException("Money.Currency is null. This indicates a serialization/deserialization bug.");

        if (Currency.Code != other.Currency.Code)
            throw new InvalidOperationException("Currency mismatch");
    }


    public override string ToString()
        => Amount.ToString($"N{Currency.MinorUnits}") + $" {Currency.Code}";
}