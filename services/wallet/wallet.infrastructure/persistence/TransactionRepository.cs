using Microsoft.EntityFrameworkCore;
using wallet.application.interfaces;
using wallet.domain.contracts;
using wallet.domain.entities;

namespace wallet.infrastructure.persistence;

public sealed class TransactionRepository(AppdbContext dbContext) : ITransactionRepository, IDisposable
{
    private readonly AppdbContext _dbContext = dbContext;
    public async Task<Guid> AddAsync(CreateTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SourceAmount <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.SourceAmount),
                "Source amount must be greater than zero.");

        if (request.FxRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.FxRate),
                "FX rate must be greater than zero.");

        if (request.SenderWalletId == request.ReceiverWalletId)
            throw new InvalidOperationException("Sender and receiver wallets cannot be identical.");

        var strategy =
            _dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await _dbContext.Database
                    .BeginTransactionAsync(cancellationToken);

            var entity = new Transaction
            {
                Id = Guid.CreateVersion7(),

                SenderWalletId = request.SenderWalletId,
                SourceCurrency = request.SourceCurrency,
                SourceAmount = request.SourceAmount,

                ReceiverWalletId = request.ReceiverWalletId,
                DestinationCurrency = request.DestinationCurrency,
                DestinationAmount = request.DestinationAmount,

                FxRate = request.FxRate,

                FeeCurrency = request.FeeCurrency,
                TransactionFee = request.TransactionFee,

                Status = TransactionStatus.Success,
                CreatedAtUtc = DateTime.UtcNow
            };

            try
            {
                _dbContext.Transactions.Add(entity);

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return entity.Id;
        });
    }

    public async Task<(IQueryable<Transaction>?, long)> GetByDateAsync(int userId,
    DateTime? startDate,
    DateTime? endDate,
    int pageIndex,
    int pageSize,
    CancellationToken cancellationToken = default!)
    {
        if (userId <= 0)
        {
            return (Enumerable.Empty<Transaction>().AsQueryable(), 0);
        }

        var walletId =
            await _dbContext.Wallet
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

        if (!Guid.TryParse(walletId.ToString(), out Guid _)) return (Enumerable.Empty<Transaction>().AsQueryable(), 0);

        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<Transaction> query =
            _dbContext.Transactions
                .AsNoTracking()
                .Include(x => x.SenderWallet)
                    .ThenInclude(x => x.User)
                .Include(x => x.ReceiverWallet)
                    .ThenInclude(x => x.User)
                .Where(x =>
                    x.SenderWalletId == walletId || x.ReceiverWalletId == walletId);

        if (startDate.HasValue)
        {
            var utcStart =
                startDate.Value.ToUniversalTime();
            query = query.Where(x =>
                x.CreatedAtUtc >= utcStart);
        }

        if (endDate.HasValue)
        {
            var utcExclusiveEnd =
                endDate.Value.Date
                    .AddDays(1).ToUniversalTime();
            query = query.Where(x =>
                x.CreatedAtUtc < utcExclusiveEnd);
        }

        return (query
            .OrderByDescending(x => x.CreatedAtUtc)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                    .AsQueryable(), await query.LongCountAsync());
    }

    public void Dispose()
    {
        _dbContext.DisposeAsync();
    }
}