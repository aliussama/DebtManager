using System.Text.Json;
using System.Text.Json.Serialization;
using DebtManager.Domain.ValueObjects.Json;

namespace DebtManager.Domain.ValueObjects;

public static class DomainJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var opt = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // ✅ Critical: stable Money/Currency serialization for event payloads
        opt.Converters.Add(new CurrencyJsonConverter());
        opt.Converters.Add(new MoneyJsonConverter());

        return opt;
    }
}
