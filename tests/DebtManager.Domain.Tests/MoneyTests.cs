using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void Add_SameCurrency_Works()
    {
        var egp = Currency.EGP;
        var a = new Money(10.555m, egp);
        var b = new Money(5.333m, egp);

        var result = a.Add(b);

        Assert.Equal(15.89m, result.Amount);
    }

    [Fact]
    public void Add_DifferentCurrency_Throws()
    {
        var egp = Currency.EGP;
        var usd = Currency.USD;

        var a = new Money(10m, egp);
        var b = new Money(5m, usd);

        Assert.Throws<InvalidOperationException>(() => a.Add(b));
    }

    [Fact]
    public void Multiply_RoundsCorrectly()
    {
        var egp = Currency.EGP;
        var a = new Money(100m, egp);

        var result = a.Multiply(0.1234m);

        Assert.Equal(12.34m, result.Amount);
    }
}
