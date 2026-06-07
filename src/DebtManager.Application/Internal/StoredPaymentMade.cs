using System.Text.Json.Serialization;
using DebtManager.Domain.Events;

namespace DebtManager.Application.Internal;

internal sealed record StoredPaymentMade(
    [property: JsonPropertyName("paymentEventId")] Guid PaymentEventId,
    [property: JsonPropertyName("payment")] PaymentMade Payment
);
