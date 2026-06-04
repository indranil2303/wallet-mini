using MediatR;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using wallet.application.interfaces;
using wallet.domain.messaging;
using wallet.domain.contracts;
using wallet.domain.entities;

namespace wallet.application.command;
public sealed class SendMoneyCommandHandler(IWalletRepository walletRepository,
    ITransactionRepository transactionRepository,
    IOutboxRepository outboxRepository,
    IUnitOfWork unitOfWork,
    IFxRateProvider fxRateProvider)
    : IRequestHandler<SendMoneyCommand, Guid>
{
    public async Task<Guid> Handle(SendMoneyCommand request, CancellationToken cancellationToken)
    {
        // 1. Basic validation
        if (request.UserId <= 0) throw new InvalidOperationException("Invalid sender.");
        if (!Guid.TryParse(request.ReceiverWalletId, out var receiverWalletId)) throw new InvalidOperationException("Invalid receiver wallet.");
        if (request.SourceAmount <= 0) throw new InvalidOperationException("Amount must be greater than zero.");
        if (request.UserId.ToString() == request.ReceiverWalletId) throw new InvalidOperationException("Cannot transfer to same wallet.");

        // 2. Fetch both entities FIRST to prevent multiple DB roundtrips (EF Core will track these)
        var sender = await walletRepository.GetWalletAsync(request.UserId.ToString(), cancellationToken);
        var receiver = await walletRepository.GetWalletAsync(request.ReceiverWalletId, cancellationToken);

        if (sender is null) throw new InvalidOperationException("Sender wallet not found.");
        if (receiver is null) throw new InvalidOperationException("Receiver wallet not found.");

        // 3. In-memory state validation (Requires your Domain Wallet entity to expose these properties)
        if (receiver.Status == WalletAccountStatus.Frozen.ToString()) throw new InvalidOperationException("Receiver wallet is frozen.");
        if (receiver.Status == WalletAccountStatus.Closed.ToString()) throw new InvalidOperationException("Receiver wallet is closed.");

        // 4. Normalize Fee to Sender's Currency
        decimal feeInSourceCurrency = request.TransactionFee;
        if (request.FeeCurrency != sender.CurrencyCode)
        {
            var feeFx = await fxRateProvider.GetRateAsync(request.FeeCurrency, sender.CurrencyCode, cancellationToken);
            if (feeFx is null) throw new InvalidOperationException("Unable to fetch FX rate for fee conversion.");
            feeInSourceCurrency = Math.Round(request.TransactionFee * feeFx.Rate, 2);
        }

        // 5. Balance Validation
        decimal totalSourceAmount = request.SourceAmount + feeInSourceCurrency;
        if (sender.Balance < totalSourceAmount)
        {
            throw new InvalidOperationException("Insufficient balance to cover amount and transaction fee.");
        }

        // 6. Validate Main FX Rate
        if (!await fxRateProvider.ValidateFxRate(sender.CurrencyCode, request.DestinationCurrency, request.FxRate))
        {
            throw new InvalidOperationException("Invalid FX rate.");
        }

        // 7. Calculate Destination Amount
        decimal destinationAmount = request.DestinationAmount;
        if (receiver.CurrencyCode != request.DestinationCurrency)
        {
            var newfx = await fxRateProvider.GetRateAsync(request.DestinationCurrency, receiver.CurrencyCode, cancellationToken);
            if (newfx is null) throw new InvalidOperationException("Unable to fetch required FX rate for destination.");
            
            // Multiply by rate to convert to receiver's native currency
            destinationAmount = Math.Round(request.DestinationAmount * newfx!.Rate, 2);
        }

        // 8. Execute Domain Logic
        try
        {
            sender.Debit(totalSourceAmount);
            receiver.Credit(destinationAmount);

            var createRequest = new CreateTransactionRequest(sender.Id, receiver.Id, request.DestinationCurrency, request.DestinationAmount, receiver.CurrencyCode, destinationAmount, request.FxRate, request.FeeCurrency, request.TransactionFee);
            var transactionId = await transactionRepository.AddAsync(createRequest, cancellationToken);

            // If using EF Core 8 tracking, explicit UpdateAsync might be unnecessary, but kept for interface consistency
            await walletRepository.UpdateAsync(sender);
            await walletRepository.UpdateAsync(receiver);

            var successMessage = "Payment Successful. Your transaction was successfully processed and confirmed.";
            var successEvent = new walletSentEvent(Guid.NewGuid(), sender.UserId, sender.CurrencyCode, totalSourceAmount, receiver.UserId, receiver.CurrencyCode, destinationAmount, successMessage, DateTime.UtcNow);
            await outboxRepository.AddAsync(KafkaConstant.WALLET_TRANSACTION_COMPLETED, JsonSerializer.Serialize(successEvent), cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return transactionId;
        }
        catch (DbUpdateConcurrencyException)
        {
            unitOfWork.ClearChangeTracker();
            var errorMessage = "Unable to reach the server at this time. The server may be offline or temporarily unavailable. Please try again later.";
            var failEvent = new walletSentEvent(Guid.NewGuid(), sender.UserId, sender.CurrencyCode, totalSourceAmount, receiver.UserId, receiver.CurrencyCode, destinationAmount, errorMessage, DateTime.UtcNow);
            await outboxRepository.AddAsync(KafkaConstant.TRANSACTION_FAILED, JsonSerializer.Serialize(failEvent), cancellationToken);
            throw new InvalidOperationException("Transaction conflict occurred.");
        }
        catch (Exception)
        {
            unitOfWork.ClearChangeTracker();
            var errorMessage = "An unexpected server error occurred while processing the request. Please try again later or contact support if the issue continues.";
            var failEvent = new walletSentEvent(Guid.NewGuid(), sender.UserId, sender.CurrencyCode, totalSourceAmount, receiver.UserId, receiver.CurrencyCode, destinationAmount, errorMessage, DateTime.UtcNow);
            await outboxRepository.AddAsync(KafkaConstant.TRANSACTION_FAILED, JsonSerializer.Serialize(failEvent), cancellationToken);
            throw;
        }
    }
}

public sealed record SendMoneyCommand(int UserId,
    string ReceiverWalletId,
    decimal SourceAmount,
    string DestinationCurrency,
    decimal DestinationAmount,
    decimal FxRate,
    string FeeCurrency,
    decimal TransactionFee) : IRequest<Guid>;


public sealed record walletSentEvent(Guid RequestId, 
    int SenderUserId,
    string SenderCurrency,
    decimal SenderAmount,
    int ReceiverUserId,
    string ReceiverCurrency,
    decimal ReceiverAmount,
    string Message, 
    DateTime CreatedAtUtc);