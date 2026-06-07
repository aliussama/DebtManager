using System.Text.Json;
using System.Text.Json.Serialization;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.ValueObjects.Json;

public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Expect object: { "amount": 123, "currency": "EGP" } (canonical)
        // Also accept: { "amount": 123, "currencyCode": "EGP" }
        // Also accept: { "amount": 123, "currency": { "code":"EGP", "minorUnits":2 } }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return new Money(0m, new Currency("UNK", 2));
        }

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        decimal amount = 0m;
        if (root.TryGetProperty("amount", out var a1) && a1.ValueKind == JsonValueKind.Number)
            amount = a1.GetDecimal();
        else if (root.TryGetProperty("Amount", out var a2) && a2.ValueKind == JsonValueKind.Number)
            amount = a2.GetDecimal();

        // 1) currencyCode (legacy)
        if (root.TryGetProperty("currencyCode", out var cc1) && cc1.ValueKind == JsonValueKind.String)
        {
            var code = cc1.GetString() ?? "UNK";
            return new Money(amount, CodeToCurrency(code));
        }

        if (root.TryGetProperty("CurrencyCode", out var cc2) && cc2.ValueKind == JsonValueKind.String)
        {
            var code = cc2.GetString() ?? "UNK";
            return new Money(amount, CodeToCurrency(code));
        }

        // 2) currency (string or object)
        if (root.TryGetProperty("currency", out var c1))
        {
            var currency = JsonSerializer.Deserialize<Currency>(c1.GetRawText(), options) ?? new Currency("UNK", 2);
            return new Money(amount, currency);
        }

        if (root.TryGetProperty("Currency", out var c2))
        {
            var currency = JsonSerializer.Deserialize<Currency>(c2.GetRawText(), options) ?? new Currency("UNK", 2);
            return new Money(amount, currency);
        }

        // fallback
        return new Money(amount, new Currency("UNK", 2));
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("amount", value.Amount);

        // Canonical "currency" uses CurrencyJsonConverter, which writes string code
        writer.WritePropertyName("currency");
        JsonSerializer.Serialize(writer, value.Currency, options);

        writer.WriteEndObject();
    }

    private static Currency CodeToCurrency(string code)
    {
        return code switch
        {
            "EGP" => Currency.EGP,
            "USD" => Currency.USD,
            "EUR" => Currency.EUR,
            _ => new Currency(code, 2)
        };
    }
}
