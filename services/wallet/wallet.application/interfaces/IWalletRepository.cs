using wallet.domain.entities;

namespace wallet.application.interfaces;

public interface IWalletRepository
{
    Task<WalletAccount?> GetWalletAsync(string identifier, CancellationToken cancellationToken = default);
    Task CreateAsync(WalletAccount wallet, CancellationToken cancellationToken);
    Task UpdateAsync(WalletAccount wallet);
    Task<bool> CheckifWalletFrozenAsync(string identifier, CancellationToken cancellationToken);
    Task<bool> CheckifWalletClosedAsync(string identifier, CancellationToken cancellationToken);
    Task<bool> CheckifWalletHasSufficientBalanceAsync(string identifier, decimal amount, CancellationToken cancellationToken);
}