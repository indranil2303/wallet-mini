using MediatR;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using wallet.application.interfaces;
using wallet.domain.messaging;
using wallet.domain.contracts;
using wallet.domain.entities;
using wallet.telemetry;
using Microsoft.Extensions.DependencyInjection;

namespace wallet.application.command;

public sealed class SendMoneyCommandHandler(IServiceScopeFactory scopeFactory,
    IWalletRepository walletRepository,
    ITransactionRepository transactionRepository,
    IOutboxRepository outboxRepository,
    IUnitOfWork unitOfWork,
    IFxRateProvider fxRateProvider,
    ICacheService<Guid> cacheService)
    : IRequestHandler<SendMoneyCommand, Guid>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<Guid> Handle(SendMoneyCommand request, CancellationToken cancellationToken)
    {
        using var activity = WalletTelemetry.ActivitySource.StartActivity("SendMoneyCommand", ActivityKind.Internal);
        activity?.SetTag("wallet.idempotency_key", request.IdempotencyKey);
        activity?.SetTag("wallet.user_id", request.UserId);
        activity?.SetTag("wallet.receiver_wallet_id", request.ReceiverWalletId);
        activity?.SetTag("wallet.source_amount", request.SourceAmount);
        activity?.SetTag("wallet.destination_currency", request.DestinationCurrency);
        activity?.SetTag("wallet.destination_amount", request.DestinationAmount);
        activity?.AddEvent(new ActivityEvent("SendMoneyCommand.Start"));

        // Checking for idempotency
        var cacheKey = $"idempotency:tx:{request.IdempotencyKey}";
        var cachedTransactionId = await cacheService.GetAsync(cacheKey, cancellationToken);
        if (Guid.TryParse(cachedTransactionId.ToString(), out var _) && cachedTransactionId != Guid.Empty)
        {
            // The transaction was already processed successfully.
            // Short-circuit the entire pipeline and return the cached ID.
            return cachedTransactionId;
        }

        // 1. Validation
        if (request.UserId <= 0) throw new InvalidOperationException("Invalid sender.");
        if (!Guid.TryParse(request.ReceiverWalletId, out var receiverWalletId)) throw new InvalidOperationException("Invalid receiver wallet.");
        if (request.SourceAmount <= 0) throw new InvalidOperationException("Amount must be greater than zero.");
        if (request.UserId.ToString() == request.ReceiverWalletId) throw new InvalidOperationException("Cannot transfer to same wallet.");

        // 2. Fetch entities sequentially.
        var sender = await walletRepository.GetWalletAsync(request.UserId.ToString(), cancellationToken);
        var receiver = await walletRepository.GetWalletAsync(request.ReceiverWalletId, cancellationToken);

        if (sender is null) throw new InvalidOperationException("Sender wallet not found.");
        if (receiver is null) throw new InvalidOperationException("Receiver wallet not found.");

        // 3. In-memory state validation
        if (receiver.Status == WalletAccountStatus.Frozen.ToString()) throw new InvalidOperationException("Receiver wallet is frozen.");
        if (receiver.Status == WalletAccountStatus.Closed.ToString()) throw new InvalidOperationException("Receiver wallet is closed.");

        // 4. Balance Validation
        decimal totalSourceAmount = request.SourceAmount + request.TransactionFee;
        if (sender.Balance < totalSourceAmount)
        {
            throw new InvalidOperationException("Insufficient balance to cover amount and transaction fee.");
        }

        // 5. Validate Main FX Rate
        if (!await fxRateProvider.ValidateFxRate(sender.CurrencyCode, request.DestinationCurrency, request.FxRate))
        {
            throw new InvalidOperationException("Invalid FX rate.");
        }

        decimal modifiedfxRate = 1m;

        // 6. Calculate Destination Amount
        decimal destinationAmount = request.DestinationAmount;
        if (!string.Equals(request.DestinationCurrency, receiver.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            var newfx = await fxRateProvider.GetRateAsync(request.DestinationCurrency, receiver.CurrencyCode, cancellationToken);
            if (newfx is null) throw new InvalidOperationException("Unable to fetch required FX rate for destination.");

            modifiedfxRate = newfx.Rate;
            destinationAmount = Math.Round(request.DestinationAmount * modifiedfxRate, 2);
        }

        Guid transactionId = Guid.Empty;
        // 7. Execute Domain Logic
        try
        {
            var createRequest = new CreateTransactionRequest(sender.Id, receiver.Id, request.DestinationCurrency, request.DestinationAmount, receiver.CurrencyCode, destinationAmount, request.FxRate, modifiedfxRate, request.FeeCurrency, request.TransactionFee, TransactionStatus.Pending);
            transactionId = await transactionRepository.AddAsync(createRequest, cancellationToken);

            var requestEvent = new WalletSentEvent(transactionId, EventConstants.SUCCESS, sender.UserId, sender.CurrencyCode, totalSourceAmount, receiver.UserId, receiver.CurrencyCode, destinationAmount, string.Empty, DateTime.UtcNow);
            await outboxRepository.AddAsync(KafkaConstant.WALLET_TRANSACTION_REQUESTED, requestEvent.RequestId, JsonSerializer.Serialize(requestEvent, JsonOptions), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            await cacheService.SetAsync(cacheKey, transactionId, TimeSpan.FromHours(12), cancellationToken);
            activity?.AddEvent(new ActivityEvent("SendMoneyCommand.Success"));
            return transactionId;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "DbUpdateConcurrencyException");
            activity?.SetTag("otel.status_description", ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);
            activity?.AddEvent(new ActivityEvent("exception"));
            await RecordTransactionFailureAsync(transactionId, sender.UserId, sender.CurrencyCode, totalSourceAmount, receiver.UserId, receiver.CurrencyCode, destinationAmount,
            "Unable to reach the server at this time. The server may be offline or temporarily unavailable. Please try again later.", cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("otel.status_description", ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);
            activity?.AddEvent(new ActivityEvent("exception"));
            await RecordTransactionFailureAsync(transactionId, sender.UserId, sender.CurrencyCode, totalSourceAmount, receiver.UserId, receiver.CurrencyCode, destinationAmount,
            "An unexpected server error occurred while processing the request. Please try again later or contact support if the issue continues.", cancellationToken);
            throw;
        }
    }

    // Redesigned to accept discrete values because 'requestEvent' might not have been instantiated if the try block fails early.
    private async Task RecordTransactionFailureAsync(Guid transactionId, int senderUserId, string senderCurrency, decimal totalSourceAmount,
        int receiverUserId, string receiverCurrency, decimal destinationAmount,
        string errorMessage, CancellationToken cancellationToken)
    {
        // Create a fresh Scope. The old DbContext threw an exception and is tainted.
        using var fallbackScope = scopeFactory.CreateScope();
        var transactionRepo = fallbackScope.ServiceProvider.GetRequiredService<ITransactionRepository>();
        var outboxRepo = fallbackScope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var uow = fallbackScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // transactionId will be Guid.Empty if the failure happened BEFORE AddAsync returned an ID
        if (transactionId != Guid.Empty)
        {
            var transaction = await transactionRepo.GetByIdAsync(transactionId.ToString(), cancellationToken);
            if (transaction is not null)
            {
                await transactionRepo.UpdateStatusAsync(transaction.Id.ToString(), TransactionStatus.Failed, cancellationToken);
            }
        }

        var failEventId = transactionId == Guid.Empty ? Guid.NewGuid() : transactionId;
        var failEvent = new WalletSentEvent(failEventId, EventConstants.FAIL, senderUserId, senderCurrency, totalSourceAmount, receiverUserId, receiverCurrency, destinationAmount, errorMessage, DateTime.UtcNow);
        await outboxRepo.AddAsync(KafkaConstant.WALLET_TRANSACTION_COMPLETED, failEvent.RequestId, JsonSerializer.Serialize(failEvent, JsonOptions), cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
    }
}

public sealed record SendMoneyCommand(string IdempotencyKey,
    int UserId,
    string ReceiverWalletId,
    decimal SourceAmount,
    string DestinationCurrency,
    decimal DestinationAmount,
    decimal FxRate,
    string FeeCurrency,
    decimal TransactionFee) : IRequest<Guid>;