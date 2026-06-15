using System.Diagnostics;
using Microsoft.Extensions.Options;
using notification.api.dispatcher;
using notification.api.contracts;
using wallet.domain.contracts;
using wallet.messaging.contracts;
using wallet.messaging.interfaces;
using wallet.telemetry;

namespace notification.api.services;

public sealed class NotificationBatchHandler : IRedisBatchHandler<WalletSentEvent>
{
    private readonly ILogger<NotificationBatchHandler> _logger;
    private readonly NotificationDispatcher _dispatcher;
    private readonly KafkaConsumerOptions _kafkaOptions;
    private readonly RedisBatchOptions _batchOptions;
    private readonly string _instanceId;

    public NotificationBatchHandler(
        ILogger<NotificationBatchHandler> logger,
        NotificationDispatcher dispatcher,
        IOptions<KafkaConsumerOptions> kafkaOptions,
        IOptions<RedisBatchOptions> batchOptions)
    {
        _logger = logger;
        _dispatcher = dispatcher;
        _kafkaOptions = kafkaOptions.Value;
        _batchOptions = batchOptions.Value;
        _instanceId = Environment.GetEnvironmentVariable("INSTANCE_ID") ?? Environment.MachineName;
    }

    public async Task ProcessBatchAsync(IReadOnlyList<RedisMessageEnvelope<WalletSentEvent>> batch, CancellationToken cancellationToken)
    {
        var topic = _kafkaOptions.Topic ?? "unknown";
        var batchId = Guid.NewGuid().ToString("N");

        using var batchActivity = WalletTelemetry.ActivitySource.StartActivity("Notification.ProcessBatch", ActivityKind.Consumer);
        batchActivity?.SetTag("messaging.system", "redis");
        batchActivity?.SetTag("messaging.destination", topic);
        batchActivity?.SetTag("messaging.batch.id", batchId);
        batchActivity?.SetTag("messaging.batch.size", batch.Count);
        batchActivity?.SetTag("messaging.instance_id", _instanceId);

        _logger.LogInformation(
            "Starting notification batch processing. Topic={Topic} BatchId={BatchId} MessageCount={MessageCount} InstanceId={InstanceId}",
            topic,
            batchId,
            batch.Count,
            _instanceId);

        try
        {
            foreach (var message in batch)
            {
                await ProcessSingleAsync(message, cancellationToken, topic, batchId, attempt: 1);
            }

            batchActivity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            batchActivity?.SetStatus(ActivityStatusCode.Error, "Notification batch processing failed.");
            batchActivity?.AddException(ex);
            _logger.LogError(ex, "Notification batch processing failed. Topic={Topic} BatchId={BatchId} InstanceId={InstanceId}", topic, batchId, _instanceId);
            throw;
        }
    }

    public async Task ProcessSingleAsync(RedisMessageEnvelope<WalletSentEvent> message, CancellationToken cancellationToken)
    {
        await ProcessSingleAsync(message, cancellationToken, _kafkaOptions.Topic ?? "unknown", Guid.NewGuid().ToString("N"), attempt: 1);
    }

    private async Task ProcessSingleAsync(RedisMessageEnvelope<WalletSentEvent> message, CancellationToken cancellationToken, string topic, string batchId, int attempt)
    {
        var payload = message.Payload;
        var traceContext = TelemetryExtensions.ExtractTraceContextFromDictionary(message.TraceHeaders);

        using var activity = WalletTelemetry.ActivitySource.StartActivity(
            "Notification.ProcessMessage",
            ActivityKind.Consumer,
            traceContext.ActivityContext);

        activity?.SetTag("messaging.system", "redis");
        activity?.SetTag("messaging.destination", topic);
        activity?.SetTag("messaging.batch.id", batchId);
        activity?.SetTag("messaging.message.id", message.Id);
        activity?.SetTag("messaging.attempt", attempt);
        activity?.SetTag("messaging.instance_id", _instanceId);
        activity?.SetTag("trace_id", activity?.Context.TraceId.ToString());
        activity?.SetTag("wallet.request_id", payload.RequestId.ToString());
        activity?.SetTag("wallet.receiver_id", payload.ReceiverUserId);

        _logger.LogInformation(
            "Processing notification message. Topic={Topic} BatchId={BatchId} MessageId={MessageId} TraceId={TraceId} Attempt={Attempt} InstanceId={InstanceId}",
            topic,
            batchId,
            message.Id,
            activity?.Context.TraceId,
            attempt,
            _instanceId);

        try
        {
            var notificationPayload = new WalletTransactionNotification(
                Type: payload.WalletEvent == EventConstants.SUCCESS ? "money_received" : "failed_event",
                RequestId: payload.RequestId,
                Currency: payload.ReceiverCurrency,
                Amount: payload.ReceiverAmount,
                Message: payload.Message,
                CreatedAtUtc: payload.CreatedAtUtc);

            await _dispatcher.SendToUserAsync(payload.ReceiverUserId.ToString(), notificationPayload);
            _logger.LogInformation(
                "Dispatched notification. Topic={Topic} BatchId={BatchId} MessageId={MessageId} RequestId={RequestId} UserId={UserId} InstanceId={InstanceId}",
                topic,
                batchId,
                message.Id,
                payload.RequestId,
                payload.ReceiverUserId,
                _instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch notification. Topic={Topic} BatchId={BatchId} MessageId={MessageId} RequestId={RequestId} InstanceId={InstanceId}", topic, batchId, message.Id, payload.RequestId, _instanceId);
            throw;
        }
    }
}