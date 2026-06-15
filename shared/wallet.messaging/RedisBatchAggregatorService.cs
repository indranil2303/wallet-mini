using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Confluent.Kafka;
using StackExchange.Redis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using wallet.messaging.contracts;
using wallet.messaging.interfaces;
using wallet.telemetry;
using Confluent.Kafka.Admin;

public sealed class RedisBatchAggregatorService<T> : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _lockKey;
    private readonly string _bufferKey;
    private readonly int _maxBufferThreshold;
    private readonly RedisBatchOptions _options;
    private readonly ConsumerConfig _consumerConfig;
    private readonly KafkaConsumerOptions _kafkaOptions;
    private readonly ILogger<RedisBatchAggregatorService<T>> _logger;
    private readonly TimeSpan _delayTime = TimeSpan.FromMilliseconds(500);
    private readonly TimeSpan _maxBatchWait = TimeSpan.FromMilliseconds(200);
    public static readonly ActivitySource ActivitySource = new("Wallet.RedisBatchAggregator");
    public static readonly Meter Meter = new("Wallet.RedisBatchAggregator.Metrics");

    private static readonly Counter<int> BatchProcessedCounter = Meter.CreateCounter<int>("wallet.redis.batch.processed_total", description: "Total batches processed");
    private static readonly Counter<int> MessageProcessedCounter = Meter.CreateCounter<int>("wallet.redis.message.processed_total", description: "Total individual messages processed");
    private static readonly Counter<int> PoisonPillCounter = Meter.CreateCounter<int>("wallet.redis.message.poison_pill_total", description: "Total discarded poison pill messages");
    private static readonly Histogram<double> BatchDurationHistogram = Meter.CreateHistogram<double>("wallet.redis.batch.duration.ms", unit: "ms", description: "Batch processing duration");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly LuaScript PopBatchScript = LuaScript.Prepare(@"
        local items = redis.call('LRANGE', @key, 0, @batchSize - 1)
        if #items > 0 then
            redis.call('LTRIM', @key, #items, -1)
        end
        return items
    ");

    private static readonly LuaScript ReleaseLockScript = LuaScript.Prepare(@"
        if redis.call('get', @key) == @token then 
            return redis.call('del', @key) 
        else 
            return 0 
        end
    ");

    public RedisBatchAggregatorService(IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaConsumerOptions> kafkaOptions,
        IOptions<RedisBatchOptions> options,
        ILogger<RedisBatchAggregatorService<T>> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _kafkaOptions = kafkaOptions.Value;
        _options = options.Value;
        _logger = logger;

        _bufferKey = $"batch:buffer:{_kafkaOptions.GroupId}:{_kafkaOptions.Topic}";
        _lockKey = $"batch:lock:{_kafkaOptions.GroupId}:{_kafkaOptions.Topic}";
        _maxBufferThreshold = _options.BatchSize * 2;
        _consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers,
            GroupId = _kafkaOptions.GroupId,
            AutoOffsetReset = _kafkaOptions.AutoOffsetReset,
            EnableAutoCommit = _kafkaOptions.EnableAutoCommit,
            FetchWaitMaxMs = _kafkaOptions.FetchWaitMaxMs,
            FetchMinBytes = _kafkaOptions.FetchMinBytes
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Started Kafka Consumer (Host: RedisBatchAggregator) for Topic: {TopicName}. Batch Size: {BatchSize}", _kafkaOptions.Topic, _options.BatchSize);

        // Run the Timer and the Consumer in parallel, isolated from one another
        var consumerTask = Task.Factory.StartNew(() => RunWithResilienceAsync(RunConsumerLoopAsync, "Consumer loop", stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        var timerTask = Task.Run(() => RunWithResilienceAsync(RunTimerFlushLoopAsync, "Timer loop", stoppingToken), stoppingToken);

        try
        {
            await Task.WhenAll(consumerTask, timerTask);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Graceful shutdown triggered for Kafka Consumer: {TopicName}.", _kafkaOptions.Topic);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in RedisBatchAggregatorService.");
        }
        finally
        {
            try { await AttemptFlushAsync(CancellationToken.None); }
            catch { /* Ignore shutdown flush failures */ }
        }
    }
    private async Task RunWithResilienceAsync(Func<CancellationToken, Task> action, string loopName, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await action(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "{LoopName} crashed. Restarting in 5s...", loopName);
                await Task.Delay(TimeSpan.FromMilliseconds(5000), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Gracefully handle cancellation that happens while sleeping/delaying
                break;
            }
        }
    }
    private async Task RunConsumerLoopAsync(CancellationToken stoppingToken)
    {
        await EnsureTopicExistsAsync(stoppingToken);

        await Task.Yield();
        using var consumer = new ConsumerBuilder<Ignore, string>(_consumerConfig).Build();
        consumer.Subscribe(_kafkaOptions.Topic);
        var db = _redis.GetDatabase();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var currentBufferLength = await db.ListLengthAsync(_bufferKey);
                if (currentBufferLength >= _maxBufferThreshold)
                {
                    _logger.LogWarning("Backpressure activated. Topic={Topic} BufferLength={BufferLength} Threshold={Threshold}", _kafkaOptions.Topic, currentBufferLength, _maxBufferThreshold);
                    await Task.Delay(_delayTime, stoppingToken);
                    continue; // Skip consuming and re-evaluate buffer size
                }

                ConsumeResult<Ignore, string>? consumeResult = null;
                try
                {
                    consumeResult = consumer.Consume(stoppingToken);
                    using var consumeActivity = ActivitySource.StartActivity("Kafka.Consume", ActivityKind.Consumer);
                    consumeActivity?.SetTag("kafka.topic", consumeResult?.Topic);
                    consumeActivity?.SetTag("kafka.partition", consumeResult?.Partition.Value);
                    consumeActivity?.SetTag("kafka.offset", consumeResult?.Offset.Value);

                    if (consumeResult is null || consumeResult.IsPartitionEOF) continue;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka network/consume error. Backing off for 5 seconds.");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                if (consumeResult?.Message == null) continue;

                var envelope = CreateEnvelope(consumeResult);
                if (envelope is null)
                {
                    consumer.Commit(consumeResult);
                    continue;
                }

                _logger.LogInformation("Consumed message -> {message}", consumeResult?.Message);
                await BufferMessageAsync(db, envelope, stoppingToken);
                consumer.Commit(consumeResult);

                // Quick localized flush if batch limit reached
                var bufferLength = await db.ListLengthAsync(_bufferKey);
                if (bufferLength >= _options.BatchSize)
                {
                    await AttemptFlushAsync(stoppingToken);
                }
                else if (bufferLength >= _maxBufferThreshold)
                {
                    // Apply backpressure if the flush couldn't keep up
                    _logger.LogWarning("Backpressure activated. BufferLength={BufferLength}", bufferLength);
                    await Task.Delay(_delayTime, stoppingToken);
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }
    private async Task RunTimerFlushLoopAsync(CancellationToken stoppingToken)
    {
        using var flushTimer = new PeriodicTimer(_maxBatchWait);
        while (await flushTimer.WaitForNextTickAsync(stoppingToken))
        {
            await AttemptFlushAsync(stoppingToken);
        }
    }
    private async Task EnsureTopicExistsAsync(CancellationToken stoppingToken)
    {
        using var adminClient = new AdminClientBuilder(
        new AdminClientConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers
        })
        .Build();

        var metadata = adminClient.GetMetadata(_kafkaOptions.Topic, TimeSpan.FromSeconds(5));

        var exists = metadata.Topics.Any(t =>
            t.Topic == _kafkaOptions.Topic &&
                t.Error.Code == ErrorCode.NoError);

        if (exists)
        {
            _logger.LogInformation("Kafka topic verified. Topic={Topic}", _kafkaOptions.Topic);
            return;
        }

        try
        {
            await adminClient.CreateTopicsAsync(
            [
                new TopicSpecification
                {
                    Name = _kafkaOptions.Topic,
                    NumPartitions = 2,
                    ReplicationFactor = 1
                }
            ]);
        }
        catch (CreateTopicsException ex)
        {
            _logger.LogError(ex, "Kafka topic creation failure!");
        }

        _logger.LogInformation("Kafka topic created. Topic={Topic}", _kafkaOptions.Topic);
    }
    private RedisMessageEnvelope<T>? CreateEnvelope(ConsumeResult<Ignore, string> consumeResult)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<T>(consumeResult.Message.Value, JsonOptions);
            if (payload is null) return null;

            var headers = ExtractKafkaHeaders(consumeResult.Message.Headers);
            return new RedisMessageEnvelope<T>(ActivityTraceId.CreateRandom().ToHexString(), headers, payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Poison pill detected. Offset {Offset}. Discarding to prevent loop.", consumeResult.Offset);
            PoisonPillCounter.Add(1, new KeyValuePair<string, object?>("topic", _kafkaOptions.Topic));
            return null; // Null allows the consumer to commit the offset and move on
        }
    }
    private async Task BufferMessageAsync(IDatabase db, RedisMessageEnvelope<T> envelope, CancellationToken stoppingToken)
    {
        var serialized = JsonSerializer.Serialize(envelope, JsonOptions);
        await db.ListRightPushAsync(_bufferKey, serialized);
    }
    private async Task AttemptFlushAsync(CancellationToken stoppingToken)
    {
        var db = _redis.GetDatabase();
        if (await db.ListLengthAsync(_bufferKey) is 0) return;

        var lockToken = Guid.NewGuid().ToString();
        bool lockAcquired = await db.StringSetAsync(_lockKey, lockToken, _options.LockTimeout, When.NotExists);
        if (!lockAcquired) return;

        try
        {
            var result = (RedisResult[]?)await db.ScriptEvaluateAsync(PopBatchScript, new
            {
                key = (RedisKey)_bufferKey,
                batchSize = _options.BatchSize
            });

            if (result is null || result.Length is 0) return;

            var batch = new List<RedisMessageEnvelope<T>>(result.Length);
            var activityLinks = new List<ActivityLink>(result.Length);

            foreach (var item in result)
            {
                try
                {
                    var itemBytes = (byte[])item!;
                    var deserialized = JsonSerializer.Deserialize<RedisMessageEnvelope<T>>(itemBytes, JsonOptions);
                    if (deserialized is null) continue;

                    batch.Add(deserialized);

                    var parentContext = TelemetryExtensions.ExtractTraceContextFromDictionary(deserialized.TraceHeaders);
                    if (parentContext.ActivityContext.TraceId != default)
                    {
                        activityLinks.Add(new ActivityLink(parentContext.ActivityContext));
                    }
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError(jsonEx, "Corrupted JSON payload in Redis buffer for topic -> {TopicName}, payload -> {payload}", _kafkaOptions.Topic, ((string?)item));
                    PoisonPillCounter.Add(1, new KeyValuePair<string, object?>("topic", _kafkaOptions.Topic));
                }
            }

            if (batch.Count is 0) return;

            using var activity = ActivitySource.StartActivity("Redis.ProcessBatch", ActivityKind.Consumer, parentContext: default, links: activityLinks);
            activity?.SetTag("redis.buffer.key", _bufferKey);
            activity?.SetTag("redis.lock.key", _lockKey);
            activity?.SetTag("messaging.system", "redis");
            activity?.SetTag("messaging.destination", _kafkaOptions.Topic);
            activity?.SetTag("messaging.batch.size", batch.Count);
            activity?.SetTag("service.instance", Environment.MachineName);

            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<IRedisBatchHandler<T>>();
            _logger.LogInformation("Batch processing started. Topic={Topic} BatchSize={BatchSize}", _kafkaOptions.Topic, batch.Count);

            try
            {
                var stopwatch = Stopwatch.StartNew();
                await handler.ProcessBatchAsync(batch, stoppingToken);

                stopwatch.Stop();
                BatchDurationHistogram.Record(stopwatch.Elapsed.TotalMilliseconds);
                activity?.SetTag("batch.duration.ms", stopwatch.ElapsedMilliseconds);

                BatchProcessedCounter.Add(1, new KeyValuePair<string, object?>("topic", _kafkaOptions.Topic));
                MessageProcessedCounter.Add(batch.Count, new KeyValuePair<string, object?>("topic", _kafkaOptions.Topic));
                activity?.SetStatus(ActivityStatusCode.Ok);

                _logger.LogInformation("Batch processed successfully. Topic={Topic} BatchSize={BatchSize} DurationMs={DurationMs}", _kafkaOptions.Topic, batch.Count, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Bulk batch processing failed. Degrading to individual processing.");
                activity?.SetStatus(ActivityStatusCode.Error, "Bulk processing failed. Initiated fallback.");
                activity?.AddException(ex);

                await ProcessIndividuallyWithIsolationAsync(batch, handler, stoppingToken);
            }
        }
        finally
        {
            await db.ScriptEvaluateAsync(ReleaseLockScript, new
            {
                key = (RedisKey)_lockKey,
                token = lockToken
            });
        }
    }
    private async Task ProcessIndividuallyWithIsolationAsync(List<RedisMessageEnvelope<T>> batch, IRedisBatchHandler<T> handler, CancellationToken stoppingToken)
    {
        foreach (var message in batch)
        {
            try
            {
                await handler.ProcessSingleAsync(message, stoppingToken);
                MessageProcessedCounter.Add(1, new KeyValuePair<string, object?>("topic", _kafkaOptions.Topic));
            }
            catch (Exception singleEx) when (singleEx is not OperationCanceledException)
            {
                _logger.LogError(singleEx, "Message discarded. Topic={Topic} MessageId={MessageId}", _kafkaOptions.Topic, message.Id);
                PoisonPillCounter.Add(1, new KeyValuePair<string, object?>("topic", _kafkaOptions.Topic));

                using var failedActivity = ActivitySource.StartActivity("Redis.ProcessPoisonPill", ActivityKind.Internal);
                failedActivity?.SetTag("messaging.message.id", message.Id);
                failedActivity?.SetStatus(ActivityStatusCode.Error, "Poison pill isolated and discarded.");
                failedActivity?.AddException(singleEx);
            }
        }
    }
    private static Dictionary<string, string> ExtractKafkaHeaders(Headers headers)
    {
        if (headers == null || headers.Count == 0) return new Dictionary<string, string>();
        var dict = new Dictionary<string, string>(headers.Count);

        foreach (var header in headers)
        {
            var bytes = header.GetValueBytes();
            dict[header.Key] = bytes is not null ? System.Text.Encoding.UTF8.GetString(bytes) : string.Empty;
        }
        return dict;
    }
}