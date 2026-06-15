using MediatR;
using wallet.domain.contracts;
using wallet.application.interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace wallet.application.query;

public sealed class GetTransactionsQueryHandler(ITransactionRepository repository, ILogger<GetTransactionsQueryHandler> logger)
: IRequestHandler<GetTransactionsQuery, PagedRecord<TransactionRecord>>
{
    private readonly ITransactionRepository _repository = repository;
    private readonly ILogger<GetTransactionsQueryHandler> _logger = logger;
    public async Task<PagedRecord<TransactionRecord>> Handle(GetTransactionsQuery request,
    CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.pageIndex);
        var pageSize = Math.Clamp(request.pageSize, 1, 100);
        var (query, totalRecords) = await _repository.GetByDateAsync(request.userId, request.startDate, request.endDate, page, pageSize);
        var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);
        var items = await query!.Select(x => new TransactionRecord(x.Id, x.ReceiverWallet.UserId == request.userId ? "CR" : "DR", x.SenderWallet.User.Alias, x.SenderWallet.Id, x.ReceiverWallet.User.Alias, x.ReceiverWallet.Id, x.SourceCurrency, x.SourceAmount, x.FxRate, x.ModifiedFxRate.GetValueOrDefault(), x.Status.ToString(), x.CreatedAtUtc)).ToListAsync() ?? [];

        return new PagedRecord<TransactionRecord>(items.ToList(), page, pageSize, totalRecords, totalPages, page > 1, page < totalPages);
    }
}

public sealed record GetTransactionsQuery(int userId, DateTime? startDate, DateTime? endDate, int pageIndex, int pageSize)
: PagedQuery<PagedRecord<TransactionRecord>>
{
    public override string CacheKey => string.Join(':',"tx", userId, startDate?.ToString("yyyyMMdd") ?? "any", endDate?.ToString("yyyyMMdd") ?? "any", pageIndex, pageSize);
}