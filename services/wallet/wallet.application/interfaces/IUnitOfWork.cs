namespace wallet.application.interfaces;

public interface IUnitOfWork
{
    Task<IDisposable> BeginTransactionAsync(CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    void ClearChangeTracker();
}