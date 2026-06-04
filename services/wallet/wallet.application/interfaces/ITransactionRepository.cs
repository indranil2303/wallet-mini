using wallet.domain.contracts;
using wallet.domain.entities;

namespace wallet.application.interfaces;

public interface ITransactionRepository
{
    Task<Guid> AddAsync(CreateTransactionRequest request,
    CancellationToken cancellationToken = default!);

    Task<(IQueryable<Transaction>?, long)> GetByDateAsync(int userId,
    DateTime? startDate,
    DateTime? endDate,
    int page,
    int pageSize,
    CancellationToken cancellationToken = default!);
}