using MediatR;
using Microsoft.EntityFrameworkCore;
using wallet.application.interfaces;
using wallet.domain.entities;

namespace wallet.application.command;
public sealed class SaveWalletStatusCommandHandler(IUnitOfWork unitOfWork,
    ICurrencyRepository currencyRepository,
    IWalletRepository walletRepository,
    IFxRateProvider fxRateProvider)
    : IRequestHandler<SaveWalletStatusCommand>
{
    public async Task Handle(SaveWalletStatusCommand request,
    CancellationToken cancellationToken)
    {
        if (request.UserId <= 0)
        {
            throw new InvalidOperationException("Invalid user.");
        }

        var currencyCode =
            request.CurrencyCode
                .Trim()
                .ToUpperInvariant();
        var isSupported =
            await currencyRepository.ValidateCurrencyAsync(currencyCode, cancellationToken);
        
        if (!isSupported)
        {
            throw new InvalidOperationException($"Unsupported currency '{currencyCode}'.");
        }

        var wallet =
            await walletRepository.GetWalletAsync(request.UserId.ToString(), cancellationToken);
        if (wallet is null)
        {
            throw new InvalidOperationException("Wallet not found.");
        }

        var currencyChanged =
            !string.Equals(wallet.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase);
        var statusActive =
            string.Equals(wallet.Status, WalletAccountStatus.Active.ToString(), StringComparison.Ordinal);

        if (!currencyChanged && statusActive)
        {
            return;
        }

        using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (currencyChanged)
            {
                var fxRate =
                    await fxRateProvider.GetRateAsync(wallet.CurrencyCode, currencyCode, cancellationToken);
                if (fxRate is null || fxRate.Rate <= 0)
                {
                    throw new InvalidOperationException($"Unable to retrieve FX rate from {wallet.CurrencyCode} to {currencyCode}.");
                }

                wallet.CurrencyCode = currencyCode;
                wallet.Balance =
                    Math.Round(wallet.Balance * fxRate.Rate, 2, MidpointRounding.AwayFromZero);
            }

            wallet.Status = WalletAccountStatus.Active.ToString();
            await walletRepository.UpdateAsync(wallet);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("The wallet was modified by another process. Please retry.");
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}

public record SaveWalletStatusCommand(int UserId, string CurrencyCode) : IRequest;