using MediatR;
using wallet.application.interfaces;
using wallet.domain.contracts;

namespace wallet.application.query;
public sealed class GetWalletSummaryQueryHandler(IWalletRepository repository)
    : IRequestHandler<GetWalletSummaryQuery, WalletRecord?>
{
    public async Task<WalletRecord?> Handle(GetWalletSummaryQuery request, CancellationToken cancellationToken)
    {
        var walletobj = await repository.GetWalletAsync(request.Session.APP_USR_ID.ToString());
        return walletobj is null ? null : new WalletRecord(walletobj.CurrencyCode, walletobj.Balance, walletobj.Status, walletobj.IsDefaultCurrencySet);
    }
}
public sealed record GetWalletSummaryQuery(AppSession Session) 
    : IRequest<WalletRecord?>;