using Microsoft.EntityFrameworkCore;
using wallet.application.interfaces;
using wallet.domain.entities;

namespace wallet.infrastructure.persistence;
public sealed class WalletRepository(AppdbContext dbContext) : IWalletRepository, IDisposable
{
    private readonly AppdbContext _dbContext = dbContext;
    public async Task<WalletAccount?> GetWalletAsync(string identifier, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        if (Guid.TryParse(identifier, out Guid walletId))
        {
            return await _dbContext.Wallet
                .Include(w => w.User)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(w => w.Id == walletId, cancellationToken);
        }

        if (int.TryParse(identifier, out int userId))
        {
            return await _dbContext.Wallet
                .Include(w => w.User)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);
        }

        return null;
    }

    public async Task CreateAsync(WalletAccount wallet, CancellationToken cancellationToken = default)
    {
        var strategy =
            _dbContext.Database.CreateExecutionStrategy();
        
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await _dbContext.Database
                    .BeginTransactionAsync(cancellationToken);
            try
            {
                await _dbContext.Wallet.AddAsync(wallet, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction
                    .RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public Task UpdateAsync(WalletAccount wallet)
    {
        _dbContext.Wallet.Update(wallet);
        return Task.CompletedTask;
    }

    public async Task<bool> CheckifWalletFrozenAsync(string identifier, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        if (Guid.TryParse(identifier, out Guid walletId))
        {
            return await _dbContext.Wallet
            .AsNoTracking()
                .AnyAsync(x => x.Id == walletId &&
                        x.Status == WalletAccountStatus.Frozen.ToString(), cancellationToken);
        }

        if (int.TryParse(identifier, out int userId))
        {
            return await _dbContext.Wallet
            .AsNoTracking()
                .AnyAsync(x => x.UserId == userId &&
                        x.Status == WalletAccountStatus.Frozen.ToString(), cancellationToken);
        }
        return false;
    }

    public async Task<bool> CheckifWalletClosedAsync(string identifier, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        if (Guid.TryParse(identifier, out Guid walletId))
        {
            return await _dbContext.Wallet
            .AsNoTracking()
                .AnyAsync(x => x.Id == walletId &&
                        x.Status == WalletAccountStatus.Closed.ToString(), cancellationToken);
        }

        if (int.TryParse(identifier, out int userId))
        {
            return await _dbContext.Wallet
            .AsNoTracking()
                .AnyAsync(x => x.UserId == userId &&
                        x.Status == WalletAccountStatus.Closed.ToString(), cancellationToken);
        }
        return false;
    }

    public async Task<bool> CheckifWalletHasSufficientBalanceAsync(string identifier, decimal amount, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        if (Guid.TryParse(identifier, out Guid walletId))
        {
            return await _dbContext.Wallet
            .AsNoTracking()
                .AnyAsync(x => x.Id == walletId &&
                        x.Balance >= amount, cancellationToken);
        }

        if (int.TryParse(identifier, out int userId))
        {
            return await _dbContext.Wallet
            .AsNoTracking()
                .AnyAsync(x => x.UserId == userId &&
                        x.Balance >= amount, cancellationToken);
        }
        return false;
    }

    public void Dispose()
    {
        _dbContext.DisposeAsync();
    }
}