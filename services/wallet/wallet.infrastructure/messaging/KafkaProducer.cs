using System.Diagnostics;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using wallet.telemetry;

namespace wallet.infrastructure.messaging;

public sealed class KafkaProducer(IOptions<KafkaSettings> options) : IKafkaProducer, IDisposable
{
    private bool _disposed;
    private readonly IProducer<string, string> _producer = CreateProducer(options.Value.BootstrapServers);
    private static IProducer<string, string> CreateProducer(string bootstrapServers)
    {
        var config = new ProducerConfig
        {
            Acks = Acks.All,
            MessageTimeoutMs = 5000,
            EnableIdempotence = true,
            BootstrapServers = bootstrapServers,
            LingerMs = 5 
        };

        return new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(string topic, string key, string message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(message);

        using var activity = WalletTelemetry.ActivitySource.StartActivity("Kafka.Publish", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.topic", topic);
        activity?.SetTag("messaging.kafka.message_key", key);
        activity?.SetTag("messaging.kafka.message", message);

        var kafkaMessage = new Message<string, string>
        {
            Key = key, // 2. Uses the domain key (e.g. WalletId) to guarantee strict ordering!
            Value = message
        };

        TelemetryExtensions.InjectKafkaTraceContext(kafkaMessage);

        var result = await _producer.ProduceAsync(topic, kafkaMessage, cancellationToken);
        if (result.Status is not PersistenceStatus.Persisted)
        {
            activity?.SetStatus(ActivityStatusCode.Error, result.Status.ToString());
            throw new InvalidOperationException($"Failed to produce message to Kafka. Status: {result.Status}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        // 3. CRITICAL: Flush forces the internal librdkafka buffer to send pending 
        // messages to the broker before the application shuts down (prevents money loss).
        try
        {
            _producer.Flush(TimeSpan.FromSeconds(10));
        }
        catch (OperationCanceledException)
        {
            // Timeout hit during flush, logs should capture this in a real system
        }
        finally
        {
            _producer.Dispose();
            _disposed = true;
        }
    }
}

public interface IKafkaProducer
{
    // Added 'key' to guarantee ordering, and a CancellationToken
    Task PublishAsync(string topic, string key, string message, CancellationToken cancellationToken = default);
}