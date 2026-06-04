using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using wallet.infrastructure.messaging;
using wallet.infrastructure.persistence;

namespace wallet.infrastructure.worker;

public class OutboxProcessorService(IServiceScopeFactory scopeFactory,
    ILogger<OutboxProcessorService> logger) : BackgroundService
{
    private const int MaxRetries = 3;
    private const int BatchSize = 50;
    private const int ProcessingDelayMs = 1000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox processor service started.");
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(ProcessingDelayMs));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Capture the count to control the timer loop
                var processedCount = await ProcessPendingMessagesAsync(stoppingToken);

                // If we processed a full batch, DO NOT WAIT. 
                // Process the next batch immediately to clear the backlog.
                if (processedCount < BatchSize)
                {
                    await timer.WaitForNextTickAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogError("Outbox processor service cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Critical error processing outbox messages. Delaying next tick.");
                await timer.WaitForNextTickAsync(stoppingToken); // Prevent CPU thrashing on database failure
            }
        }
    }

    private async Task<int> ProcessPendingMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppdbContext>();
        var producer = scope.ServiceProvider.GetRequiredService<IKafkaProducer>();

        // Exclude messages that have exceeded MaxRetries, or they will clog the queue forever.
        var messages = await db.OutboxMessages
            .Where(x => !x.Processed && (x.ProcessingAttempts == null || x.ProcessingAttempts < MaxRetries))
            .OrderBy(x => x.OccurredOnUtc)
            .Take(BatchSize)
            .ToListAsync(stoppingToken);

        if (messages.Count == 0)
            return 0;

        foreach (var message in messages)
        {
            try
            {
                // Ensure you are passing the actual payload string/JSON
                // If message.ToString() does not return the JSON payload, use message.Payload or message.Data
                await producer.PublishAsync(message.EventKey, message.Payload);
                message.ProcessingAttempts = 1;
                message.Processed = true;
            }
            catch (Exception ex)
            {
                message.ProcessingAttempts = (message.ProcessingAttempts ?? 0) + 1;
                if (message.ProcessingAttempts >= MaxRetries)
                {
                    // By leaving Processed = false but hitting MaxRetries, 
                    // the updated LINQ query above will now safely ignore this dead message.
                    logger.LogError(ex, "Failed to publish message {MessageId} permanently after {Attempts} attempts.", message.Id, message.ProcessingAttempts);
                }
                else
                {
                    logger.LogWarning(ex, "Failed to publish message {MessageId}. Attempt {Current}/{Max}", message.Id, message.ProcessingAttempts, MaxRetries);
                }
            }
        }

        await db.SaveChangesAsync(stoppingToken);
        return messages.Count;
    }
}