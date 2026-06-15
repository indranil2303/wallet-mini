using Microsoft.EntityFrameworkCore;
using wallet.application.interfaces;
using wallet.domain.entities;

namespace wallet.infrastructure.persistence;

public sealed class OutboxRepository(AppdbContext dbContext) : IOutboxRepository, IDisposable
{
    private readonly AppdbContext _dbContext = dbContext;
    public async Task AddAsync(string eventKey, Guid requestId, string payload, CancellationToken cancellationToken)
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
                    RequestId = requestId,
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
    public async Task<bool> CheckIfOutboxEventExists(string identifier, string eventKey, CancellationToken cancellationToken = default!)
    {
        if (string.IsNullOrWhiteSpace(eventKey) ||
        !Guid.TryParse(identifier, out Guid requestId))
        {
            return false;
        }

        return await _dbContext.OutboxMessages
            .AsNoTracking().AnyAsync(w => w.EventKey == eventKey && w.RequestId == requestId, cancellationToken);
    }

    public void Dispose()
    {
        _dbContext.DisposeAsync();
    }
}