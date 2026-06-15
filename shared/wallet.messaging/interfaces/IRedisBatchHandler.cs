using wallet.messaging.contracts;

namespace wallet.messaging.interfaces;

public interface IRedisBatchHandler<T>
{
    // Executes the fast, optimistic bulk operation
    Task ProcessBatchAsync(IReadOnlyList<RedisMessageEnvelope<T>> batch, CancellationToken cancellationToken);
    
    // Executes single-message processing when bulk fails (Poison-pill isolation)
    Task ProcessSingleAsync(RedisMessageEnvelope<T> message, CancellationToken cancellationToken);
}