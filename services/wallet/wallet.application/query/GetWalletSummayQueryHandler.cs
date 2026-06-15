using MediatR;
using Microsoft.Extensions.Logging;
using wallet.application.interfaces;
using wallet.domain.contracts;

namespace wallet.application.query;

public sealed class GetWalletSummaryQueryHandler(IWalletRepository repository,
ILogger<GetWalletSummaryQueryHandler> logger)
    : IRequestHandler<GetWalletSummaryQuery, WalletRecord?>
{
    public async Task<WalletRecord?> Handle(GetWalletSummaryQuery request, CancellationToken cancellationToken)
    {
        var walletState = await repository.GetWalletSummaryAsync(request.Session.APP_USR_ID.ToString(), cancellationToken);
        logger.LogInformation("walletState -> {walletState}", walletState);

        return walletState is { } state
            ? new WalletRecord(state.currenyCode, state.balance, state.status.ToString(), state.isDefaultCurrencySet)
            : null;
    }
}
public sealed record GetWalletSummaryQuery(AppSession Session)
    : IRequest<WalletRecord?>;