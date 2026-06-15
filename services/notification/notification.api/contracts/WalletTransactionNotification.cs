using System.Text.Json.Serialization;

namespace notification.api.contracts;
public sealed record WalletTransactionNotification([property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("requestId")] Guid RequestId,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("message")] string Message, 
    [property: JsonPropertyName("createdAtUtc")] DateTime CreatedAtUtc
);