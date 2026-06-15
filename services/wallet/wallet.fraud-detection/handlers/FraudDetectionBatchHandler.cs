using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.ML;
using Microsoft.Extensions.Options;
using wallet.application.interfaces;
using wallet.domain.contracts;
using wallet.domain.entities;
using wallet.domain.messaging;
using wallet.fraud_detection.contracts;
using wallet.messaging.contracts;
using wallet.messaging.interfaces;
using wallet.telemetry;

namespace wallet.fraud_detection;

public sealed class FraudDetectionBatchHandler : IRedisBatchHandler<WalletSentEvent>
{
    private readonly ILogger<FraudDetectionBatchHandler> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PredictionEnginePool<FraudModelInput, FraudPrediction> _predictionEngine;
    private readonly KafkaConsumerOptions _kafkaOptions;
    private readonly RedisBatchOptions _batchOptions;
    private readonly string _instanceId;

    public FraudDetectionBatchHandler(
        ILogger<FraudDetectionBatchHandler> logger,
        IServiceScopeFactory scopeFactory,
        PredictionEnginePool<FraudModelInput, FraudPrediction> predictionEngine,
        IOptions<KafkaConsumerOptions> kafkaOptions,
        IOptions<RedisBatchOptions> batchOptions)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _predictionEngine = predictionEngine;
        _kafkaOptions = kafkaOptions.Value;
        _batchOptions = batchOptions.Value;
        _instanceId = Environment.GetEnvironmentVariable("INSTANCE_ID") ?? Environment.MachineName;
    }

    public async Task ProcessBatchAsync(IReadOnlyList<RedisMessageEnvelope<WalletSentEvent>> batch, CancellationToken cancellationToken)
    {
        var topic = _kafkaOptions.Topic ?? "unknown";
        var batchId = Guid.NewGuid().ToString("N");

        using var activity = WalletTelemetry.ActivitySource.StartActivity("FraudDetection.ProcessBatch", ActivityKind.Consumer);
        activity?.SetTag("messaging.system", "redis");
        activity?.SetTag("messaging.destination", topic);
        activity?.SetTag("messaging.batch.id", batchId);
        activity?.SetTag("messaging.batch.size", batch.Count);
        activity?.SetTag("messaging.instance_id", _instanceId);

        _logger.LogInformation(
            "Starting fraud detection batch. Topic={Topic} BatchId={BatchId} MessageCount={MessageCount} InstanceId={InstanceId}",
            topic,
            batchId,
            batch.Count,
            _instanceId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var walletRepo = scope.ServiceProvider.GetRequiredService<IWalletRepository>();
            var transRepo = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
            var outboxRepo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            foreach (var message in batch)
            {
                await ProcessSingleAsync(message, cancellationToken, topic, batchId, attempt: 1, walletRepo, transRepo, outboxRepo, uow);
            }

            await uow.SaveChangesAsync(cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Bulk fraud batch processing failed. Topic={Topic} BatchId={BatchId} InstanceId={InstanceId}. Falling back to individual handling.", topic, batchId, _instanceId);
            activity?.SetStatus(ActivityStatusCode.Error, "Bulk processing failed.");
            activity?.AddException(ex);
            throw;
        }
    }

    public async Task ProcessSingleAsync(RedisMessageEnvelope<WalletSentEvent> message, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var walletRepo = scope.ServiceProvider.GetRequiredService<IWalletRepository>();
        var transRepo = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
        var outboxRepo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await ProcessSingleAsync(message, cancellationToken, _kafkaOptions.Topic ?? "unknown", Guid.NewGuid().ToString("N"), attempt: 1, walletRepo, transRepo, outboxRepo, uow);
        await uow.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessSingleAsync(
        RedisMessageEnvelope<WalletSentEvent> message,
        CancellationToken cancellationToken,
        string topic,
        string batchId,
        int attempt,
        IWalletRepository walletRepo,
        ITransactionRepository transRepo,
        IOutboxRepository outboxRepo,
        IUnitOfWork uow)
    {
        var payload = message.Payload;
        var traceContext = TelemetryExtensions.ExtractTraceContextFromDictionary(message.TraceHeaders);

        using var activity = WalletTelemetry.ActivitySource.StartActivity(
            "FraudDetection.ProcessSingle",
            ActivityKind.Internal,
            traceContext.ActivityContext);

        activity?.SetTag("messaging.system", "redis");
        activity?.SetTag("messaging.destination", topic);
        activity?.SetTag("messaging.batch.id", batchId);
        activity?.SetTag("messaging.message.id", message.Id);
        activity?.SetTag("messaging.attempt", attempt);
        activity?.SetTag("messaging.instance_id", _instanceId);
        activity?.SetTag("trace_id", activity?.Context.TraceId.ToString());
        activity?.SetTag("wallet.request_id", payload.RequestId.ToString());
        activity?.SetTag("wallet.sender_id", payload.SenderUserId);

        _logger.LogInformation(
            "Processing fraud message. Topic={Topic} BatchId={BatchId} MessageId={MessageId} TraceId={TraceId} Attempt={Attempt} InstanceId={InstanceId}",
            topic,
            batchId,
            message.Id,
            activity?.Context.TraceId,
            attempt,
            _instanceId);

        try
        {
            await ApplyBusinessLogicAsync(payload, walletRepo, transRepo, outboxRepo, cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            _logger.LogInformation(
                "Completed fraud processing message. Topic={Topic} BatchId={BatchId} MessageId={MessageId} RequestId={RequestId} InstanceId={InstanceId}",
                topic,
                batchId,
                message.Id,
                payload.RequestId,
                _instanceId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Message processing failed.");
            activity?.AddException(ex);
            _logger.LogError(ex, "Failed processing fraud message. Topic={Topic} BatchId={BatchId} MessageId={MessageId} RequestId={RequestId} InstanceId={InstanceId}", topic, batchId, message.Id, payload.RequestId, _instanceId);
            throw;
        }
    }

    private async Task ApplyBusinessLogicAsync(WalletSentEvent requestedEvent,
    IWalletRepository walletRepository,
    ITransactionRepository transactionRepository,
    IOutboxRepository outboxRepository,
    CancellationToken stoppingToken)
    {
        var requestIdStr = requestedEvent.RequestId.ToString();
        var senderIdStr = requestedEvent.SenderUserId.ToString();
        var receiverIdStr = requestedEvent.ReceiverUserId.ToString();

        using var activity = WalletTelemetry.ActivitySource.StartActivity("FraudDetection.ApplyBusinessLogic", ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag("wallet.request_id", requestIdStr);
            activity.SetTag("wallet.sender_id", senderIdStr);
            activity.SetTag("wallet.receiver_id", receiverIdStr);
            activity.SetTag("wallet.amount", requestedEvent.SenderAmount);
        }

        var outboxEventExists = await outboxRepository.CheckIfOutboxEventExists(requestIdStr, KafkaConstant.WALLET_TRANSACTION_COMPLETED, stoppingToken);
        if (outboxEventExists)
        {
            _logger.LogWarning("The event is already processed for {RequestId}", requestIdStr);
            return;
        }

        var transaction = await transactionRepository.GetByIdAsync(requestIdStr, stoppingToken);
        if (transaction is null)
        {
            _logger.LogWarning("Transaction record not found for {RequestId}", requestIdStr);
            return;
        }

        if (transaction.Status is TransactionStatus.Success or TransactionStatus.Failed)
        {
            _logger.LogInformation("Transaction {Id} already processed with status {Status}.", transaction.Id, transaction.Status);
            return;
        }

        var sender = await walletRepository.GetWalletAsync(senderIdStr, stoppingToken);
        var receiver = await walletRepository.GetWalletAsync(receiverIdStr, stoppingToken);
        if (sender is null || receiver is null)
        {
            _logger.LogError("Sender or Receiver wallet not found for RequestId: {RequestId}", requestIdStr);
            transaction.Status = TransactionStatus.Failed;
            var missingEvent = requestedEvent with { WalletEvent = EventConstants.FAIL, Message = "Transaction blocked: Invalid wallet accounts." };
            await outboxRepository.AddAsync(KafkaConstant.WALLET_TRANSACTION_COMPLETED, missingEvent.RequestId, JsonSerializer.Serialize(missingEvent), stoppingToken);
            return;
        }

        var oldBalanceOrg = (float)sender.Balance;
        var oldBalanceDest = (float)receiver.Balance;
        var senderAmountFloat = (float)requestedEvent.SenderAmount;
        var receiverAmountFloat = (float)requestedEvent.ReceiverAmount;

        var mlInput = new FraudModelInput
        {
            Type = "TRANSFER",
            Step = 1,
            Amount = senderAmountFloat,
            NameOrig = senderIdStr,
            NameDest = receiverIdStr,
            OldBalanceOrg = oldBalanceOrg,
            NewBalanceOrig = oldBalanceOrg - senderAmountFloat,
            OldBalanceDest = oldBalanceDest,
            NewBalanceDest = oldBalanceDest + receiverAmountFloat,
            IsFlaggedFraud = 0f,
            IsFraud = 0f
        };

        var prediction = _predictionEngine.Predict(modelName: "FraudDetection", mlInput);
        var confidenceScore = (prediction.Score?.Length > 1 ? prediction.Score[1] : 0) * 100;
        _logger.LogWarning("Fraud Confidence: {Confidence}%", confidenceScore);

        TransactionStatus transactionEventStatus;
        EventConstants walletEventConstant;
        string eventMessage;

        if (prediction.IsFraud)
        {
            _logger.LogWarning("Security Block: Transaction {RequestId} flagged as fraud.", requestIdStr);
            transactionEventStatus = TransactionStatus.Failed;
            walletEventConstant = EventConstants.FAIL;
            eventMessage = "Transaction blocked due to suspicious pattern matching.";
        }
        else
        {
            _logger.LogInformation("Transaction {RequestId} cleared fraud assessment.", requestIdStr);
            sender.Debit(requestedEvent.SenderAmount);
            receiver.Credit(requestedEvent.ReceiverAmount);
            transactionEventStatus = TransactionStatus.Success;
            walletEventConstant = EventConstants.SUCCESS;
            eventMessage = "Payment Received. Transaction has been successfully processed and credited to your wallet.";
        }

        await transactionRepository.UpdateStatusAsync(transaction.Id.ToString(), transactionEventStatus, stoppingToken);
        await walletRepository.UpdateAsync(sender);
        await walletRepository.UpdateAsync(receiver);
        var finalEvent = requestedEvent with { WalletEvent = walletEventConstant, Message = eventMessage };
        await outboxRepository.AddAsync(KafkaConstant.WALLET_TRANSACTION_COMPLETED, finalEvent.RequestId, JsonSerializer.Serialize(finalEvent), stoppingToken);
    }
}