using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using wallet.application.interfaces;
using wallet.infrastructure.messaging;
using wallet.infrastructure.persistence;
using wallet.telemetry;

namespace wallet.infrastructure.worker;

public class OutboxProcessorService(IOutboxTrigger trigger,
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxProcessorService> logger) : BackgroundService
{
    private const int MaxRetries = 3;
    private const int BatchSize = 50;
    private readonly TimeSpan MaxIdleTimeout = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox processor service started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedCount = await ProcessPendingMessagesAsync(stoppingToken);

                // If a full batch was processed, loop immediately to clear backlog
                if (processedCount == BatchSize)
                {
                    continue;
                }

                // Zero CPU Wait: Sleep until the API triggers us, OR the 30-second fallback timeout hits
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeoutCts.CancelAfter(MaxIdleTimeout);

                try
                {
                    await trigger.WaitForMessageAsync(timeoutCts.Token);
                    trigger.TryConsumeMessage();
                }
                catch (OperationCanceledException)
                {
                    // Expected behavior when the 30s timeout hits or application shuts down.
                    // The loop will simply restart and check the database.
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Critical error processing outbox. Backing off for 5 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<int> ProcessPendingMessagesAsync(CancellationToken stoppingToken)
    {
        using var activity = WalletTelemetry.ActivitySource.StartActivity("Outbox.ProcessPendingMessages", ActivityKind.Internal);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppdbContext>();
        var producer = scope.ServiceProvider.GetRequiredService<IKafkaProducer>();

        var messages = await db.OutboxMessages
            .AsTracking()
            .Where(x => !x.Processed && (x.ProcessingAttempts == null || x.ProcessingAttempts < MaxRetries))
                .OrderBy(x => x.OccurredOnUtc)
                .Take(BatchSize)
                    .ToListAsync(stoppingToken);

        if (messages.Count == 0) return 0;

        //ISOLATE: Extract raw data to safely use across multiple threads without corrupting EF Core's DbContext
        var publishTasks = messages.Select(async msg =>
        {
            try
            {
                logger.LogInformation("Topic -> {EventKey}, Key -> {RequestId}, Payload -> {Payload}", msg.EventKey, msg.RequestId, msg.Payload);
                await producer.PublishAsync(msg.EventKey, msg.RequestId.ToString(), msg.Payload, stoppingToken);

                return (Id: msg.Id, Success: true, Error: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Id: msg.Id, Success: false, Error: ex);
            }
        });

        //CONCURRENCY: Publish all messages to Kafka simultaneously
        var results = await Task.WhenAll(publishTasks);

        //SYNCHRONIZE: Apply results back to the EF Core tracked entities safely on the main thread
        foreach (var result in results)
        {
            var entity = messages.First(m => m.Id == result.Id);

            if (result.Success)
            {
                entity.Processed = true;
                entity.ProcessingAttempts = 1;
            }
            else
            {
                entity.ProcessingAttempts = (entity.ProcessingAttempts ?? 0) + 1;

                if (entity.ProcessingAttempts >= MaxRetries) logger.LogError(result.Error, "Message {MessageId} permanently failed after {Attempts} attempts.", entity.Id, entity.ProcessingAttempts);
                else logger.LogWarning(result.Error, "Failed to publish message {MessageId}. Attempt {Current}/{Max}", entity.Id, entity.ProcessingAttempts, MaxRetries);
            }
        }

        await db.SaveChangesAsync(stoppingToken);
        return messages.Count;
    }
}