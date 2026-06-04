using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.SignalR.Client;
using System.Text;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using notification.consumer.contracts;
using Confluent.Kafka.Admin;
using Microsoft.AspNetCore.SignalR;

namespace notification.consumer.consumers;

public sealed class TransactionCompletedConsumer : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly HubConnection _hubConnection;
    private readonly ILogger<TransactionCompletedConsumer> _logger;

    public TransactionCompletedConsumer(IConfiguration configuration,
        ILogger<TransactionCompletedConsumer> logger)
    {
        _configuration = configuration;
        _logger = logger;

        var secret = configuration["Jwt:Secret"]!;
        var issuer = configuration["Jwt:Issuer"]!;
        var audience = configuration["Jwt:Audience"]!;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(issuer,
            audience,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(configuration["NotificationApi:HubUrl"]!,
                options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(jwt)!;
                })
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureHubConnected(stoppingToken);

        // 1. Ensure topics exist before attempting to consume
        await EnsureTopicsExistAsync(stoppingToken);

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _configuration["Kafka:BootstrapServers"],
            GroupId = _configuration["Kafka:GroupId"],
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = true,
            ReconnectBackoffMs = 5000,
            ReconnectBackoffMaxMs = 10000,
            SocketConnectionSetupTimeoutMs = 30000
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();

        // 2. Subscribe is now safe because the topics are guaranteed to exist
        consumer.Subscribe(new[]
        {
            _configuration["Kafka:Topic"],
            _configuration["Kafka:FailureTopic"]
        });

        while (!stoppingToken.IsCancellationRequested)
        {
            var consumed = consumer.Consume(stoppingToken);
            if (consumed?.Message?.Value is null)
            {
                continue;
            }

            try
            {
                var notification = JsonSerializer.Deserialize<TransactionEvent>(consumed.Message.Value);
                if (notification is null)
                {
                    consumer.Commit(consumed);
                    continue;
                }
                
                //
                _logger.LogInformation("Kafka JSON payload: {Payload}", consumed.Message.Value);
                await PublishNotificationAsync(notification, stoppingToken);
                consumer.Commit(consumed);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Invalid Kafka JSON payload: {Payload}", consumed.Message.Value);
                // consumer.Commit(consumed);
                continue;
            }
            catch (ConsumeException ex)
            {
                _logger.LogWarning(ex, "Kafka consume failed.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Uses the AdminClient to verify and create topics if they are missing on startup.
    /// </summary>
    private async Task EnsureTopicsExistAsync(CancellationToken cancellationToken)
    {
        var bootstrapServers = _configuration["Kafka:BootstrapServers"];
        var topic = _configuration["Kafka:Topic"];
        var failureTopic = _configuration["Kafka:FailureTopic"];

        if (string.IsNullOrEmpty(bootstrapServers) || string.IsNullOrEmpty(topic) || string.IsNullOrEmpty(failureTopic))
        {
            _logger.LogWarning("Kafka configuration missing. Cannot verify topics.");
            return;
        }

        using var adminClient = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();

        // Loop indefinitely until cancelled or successful
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await adminClient.CreateTopicsAsync(new[]
                {
                    new TopicSpecification { Name = topic, ReplicationFactor = 1, NumPartitions = 1 },
                    new TopicSpecification { Name = failureTopic, ReplicationFactor = 1, NumPartitions = 1 },
                    new TopicSpecification { Name = "healthchecks-topic", ReplicationFactor = 1, NumPartitions = 1 }
                });

                _logger.LogInformation("Kafka topics successfully provisioned.");
                return; // Break out of the loop on success
            }
            catch (CreateTopicsException e)
            {
                bool allTopicsExist = true;
                foreach (var result in e.Results)
                {
                    if (result.Error.Code != ErrorCode.TopicAlreadyExists)
                    {
                        allTopicsExist = false;
                        _logger.LogError($"An error occurred creating topic {result.Topic}: {result.Error.Reason}");
                    }
                }

                if (allTopicsExist)
                {
                    _logger.LogInformation("Kafka topics already exist. Proceeding...");
                    return; // Break out of the loop on success
                }

                // If it failed for another reason, wait and retry
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (Exception)
            {
                // We catch a generic Exception here (like connection refused)
                _logger.LogWarning("Kafka broker is not reachable yet. Retrying in 5 seconds...");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private async Task PublishNotificationAsync(TransactionEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await _hubConnection.InvokeAsync("SendNotificationToUser",
                notification.ReceiverUserId.ToString(),
                new WalletTransactionNotification("money_received", notification.RequestId, notification.ReceiverCurrency, notification.ReceiverAmount, notification.Message, notification.CreatedAtUtc),
                cancellationToken);

            await _hubConnection.InvokeAsync("SendNotificationToUser",
                notification.SenderUserId.ToString(),
                new WalletTransactionNotification("money_sent", notification.RequestId, notification.SenderCurrency, notification.SenderAmount, notification.Message, notification.CreatedAtUtc),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR notification failed.");
        }
    }

    private async Task EnsureHubConnected(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_hubConnection.State == HubConnectionState.Connected)
                {
                    return;
                }

                await _hubConnection.StartAsync(cancellationToken);
                return;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }
}