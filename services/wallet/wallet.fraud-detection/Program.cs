using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;
using wallet.application.interfaces;
using wallet.domain.contracts;
using wallet.fraud_detection;
using wallet.fraud_detection.contracts;
using wallet.fraud_detection.extensions;
using StackExchange.Redis;
using wallet.infrastructure.persistence;
using wallet.messaging.contracts;
using wallet.messaging.interfaces;
using wallet.telemetry;

// 1. Bootstrap Serilog immediately to catch host-level startup crashes
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.With<ActivityTraceEnricher>()
    .WriteTo.Console(new RenderedCompactJsonFormatter()) // Outputs logs as JSON for ELK/Datadog
    .CreateLogger();

try
{
    Log.Information("Starting up the Fraud Detection Worker...");
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddWalletTelemetry(builder.Configuration, "wallet-fraud-worker");

    builder.Services.AddDbContextPool<AppdbContext>(options =>
    {
        options.UseNpgsql(
            builder.Configuration["ConnectionStrings:Postgres"]!,
            npgsql =>
            {
                npgsql.EnableRetryOnFailure(3);
                npgsql.CommandTimeout(15);
            });

    }, poolSize: 512);

    // 2. To route all internal framework logs through our Serilog pipeline
    builder.Services.AddSerilog();

    var kafkaConfig = builder.Configuration.GetSection("Kafka").Get<KafkaConfig>()
        ?? throw new InvalidOperationException("Kafka values missing in configuration.");

    // 3. Fallback path for smoother local development
    var mlModelPath = builder.Configuration["MLModelPath"]
        ?? Path.Combine(AppContext.BaseDirectory, "models", "FraudDetection.zip");

    // 4. Register the core dependencies
    builder.Services.AddFraudDetectionConfiguration(kafkaConfig, mlModelPath);

    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
        ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"] 
            ?? throw new InvalidOperationException("Redis:ConnectionString must be configured")));

    builder.Services.Configure<RedisBatchOptions>(options =>
    {
        options.BatchSize = builder.Configuration.GetValue<int?>("Redis:BatchSize") ?? 100;
        options.ThrottlingInterval = builder.Configuration.GetValue<TimeSpan?>("Redis:ThrottlingInterval") ?? TimeSpan.FromSeconds(2);
        options.LockTimeout = builder.Configuration.GetValue<TimeSpan?>("Redis:LockTimeout") ?? TimeSpan.FromSeconds(30);
    });

    builder.Services.Configure<KafkaConsumerOptions>(options => 
    {
        options.BootstrapServers = builder.Configuration["Kafka:BootstrapServers"] ?? "kafka:9092";
        options.GroupId = builder.Configuration["Kafka:GroupId"] ?? "wallet-fraud-detect-group";
        options.Topic = builder.Configuration["Kafka:Topic"] ?? "wallet.transaction.requested";
    });

    builder.Services.AddSingleton<IRedisBatchHandler<WalletSentEvent>, FraudDetectionBatchHandler>();
    builder.Services.AddHostedService<RedisBatchAggregatorService<WalletSentEvent>>();

    builder.Services.AddScoped<IWalletRepository, WalletRepository>();
    builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
    builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();
    builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    // If the DI container fails to build or a config is completely invalid, we catch it here.
    Log.Fatal(ex, "The application failed to start correctly.");
}
finally
{
    // 6. Ensure the log buffer is completely flushed to the console/network before the app exits
    Log.CloseAndFlush();
}