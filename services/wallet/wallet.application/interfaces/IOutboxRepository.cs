namespace wallet.application.interfaces;

public interface IOutboxRepository
{
    Task AddAsync(string eventKey, string payload, CancellationToken cancellationToken = default!);
}