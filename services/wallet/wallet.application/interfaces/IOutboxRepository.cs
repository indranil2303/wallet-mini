namespace wallet.application.interfaces;

public interface IOutboxRepository
{
    Task AddAsync(string eventKey, Guid requestId, string payload, CancellationToken cancellationToken = default!);
    Task<bool> CheckIfOutboxEventExists(string identifier, string eventKey, CancellationToken cancellationToken = default!);
}