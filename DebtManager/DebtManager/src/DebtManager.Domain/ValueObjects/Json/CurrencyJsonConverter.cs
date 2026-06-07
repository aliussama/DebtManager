using System.Text.Json;
using System.Text.Json.Serialization;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.ValueObjects.Json;

public sealed class CurrencyJsonConverter : JsonConverter<Currency>
{
    public override Currency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Accept: "EGP"
        if (reader.TokenType == JsonTokenType.String)
        {
            var code = reader.GetString() ?? "UNK";
            return FromCodeOrDefault(code);
        }

        // Accept: { "code":"EGP", "minorUnits":2 } (or casing variants)
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            string? code = null;

            if (root.TryGetProperty("code", out var c1) && c1.ValueKind == JsonValueKind.String)
                code = c1.GetString();
            else if (root.TryGetProperty("Code", out var c2) && c2.ValueKind == JsonValueKind.String)
                code = c2.GetString();

            if (string.IsNullOrWhiteSpace(code))
                return new Currency("UNK", 2);

            // minorUnits (or MinorUnits)
            if (root.TryGetProperty("minorUnits", out var mu1) && mu1.ValueKind == JsonValueKind.Number)
                return new Currency(code!, mu1.GetInt32());

            if (root.TryGetProperty("MinorUnits", out var mu2) && mu2.ValueKind == JsonValueKind.Number)
                return new Currency(code!, mu2.GetInt32());

            // Legacy name: decimalPlaces / DecimalPlaces
            if (root.TryGetProperty("decimalPlaces", out var dp1) && dp1.ValueKind == JsonValueKind.Number)
                return new Currency(code!, dp1.GetInt32());

            if (root.TryGetProperty("DecimalPlaces", out var dp2) && dp2.ValueKind == JsonValueKind.Number)
                return new Currency(code!, dp2.GetInt32());

            // Default by code map
            return FromCodeOrDefault(code!);
        }

        reader.Skip();
        return new Currency("UNK", 2);
    }

    public override void Write(Utf8JsonWriter writer, Currency value, JsonSerializerOptions options)
    {
        // Canonical: write as string code to keep payload small & stable
        writer.WriteStringValue(value.Code);
    }

    private static Currency FromCodeOrDefault(string code)
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
