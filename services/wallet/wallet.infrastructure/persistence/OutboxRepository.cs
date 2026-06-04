using Microsoft.EntityFrameworkCore;
using wallet.application.interfaces;
using wallet.domain.entities;

namespace wallet.infrastructure.persistence;

public sealed class OutboxRepository(AppdbContext dbContext) : IOutboxRepository, IDisposable
{
    private readonly AppdbContext _dbContext = dbContext;
    public async Task AddAsync(string eventKey, string payload, CancellationToken cancellationToken = default!)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var outboxMessage = new OutboxMessage
                {
                    EventKey = eventKey,
                    Payload = payload,
                    OccurredOnUtc = DateTime.UtcNow,
                    Processed = false
                };
                
                await _dbContext.OutboxMessages.AddAsync(outboxMessage, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public void Dispose()
    {
        _dbContext.DisposeAsync();
    }
}