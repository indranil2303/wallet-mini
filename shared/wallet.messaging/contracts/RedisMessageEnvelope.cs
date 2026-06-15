namespace wallet.messaging.contracts;

public record  RedisMessageEnvelope<T>
(
    string Id,
    Dictionary<string, string> TraceHeaders,
    T Payload
);