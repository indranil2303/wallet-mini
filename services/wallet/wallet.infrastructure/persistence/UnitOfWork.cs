using Microsoft.EntityFrameworkCore;
using wallet.application.interfaces;

namespace wallet.infrastructure.persistence;

public sealed class UnitOfWork(AppdbContext dbContext) : IUnitOfWork, IDisposable
{
    private readonly AppdbContext _dbContext = dbContext;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            Console.WriteLine($"Error saving changes: {ex.Message}");
            throw;
        }
    }

    public async Task<IDisposable> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await _dbContext.Database.CommitTransactionAsync(cancellationToken);
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        await _dbContext.Database.RollbackTransactionAsync(cancellationToken);
    }
    public void ClearChangeTracker()
    {
        dbContext.ChangeTracker.Clear();
    }
    public void Dispose()
    {
        _dbContext?.Dispose();
    }
}