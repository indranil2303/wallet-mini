using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace wallet.infrastructure.messaging;

public sealed class KafkaProducer(IOptions<KafkaSettings> options) : IKafkaProducer, IDisposable
{
    private readonly IProducer<string, string> _producer = CreateProducer(options.Value.BootstrapServers);
    private bool _disposed;
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

    public async Task PublishAsync(string key, string message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.Topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(message);

        var kafkaMessage = new Message<string, string>
        {
            Key = key, // 2. Uses the domain key (e.g. WalletId) to guarantee strict ordering!
            Value = message
        };

        var result = await _producer.ProduceAsync(options.Value.Topic, kafkaMessage, cancellationToken);
        if (result.Status != PersistenceStatus.Persisted)
        {
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
    Task PublishAsync(string key, string message, CancellationToken cancellationToken = default);
}