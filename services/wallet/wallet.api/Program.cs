using StackExchange.Redis;
using System.Text;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using wallet.api;
using wallet.api.extensions;
using wallet.application.query;
using wallet.application.interfaces;
using wallet.infrastructure.messaging;
using wallet.infrastructure.persistence;
using wallet.infrastructure.worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using wallet.application.behaviour;
using Serilog;
using wallet.telemetry;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.With<ActivityTraceEnricher>()
        .WriteTo.Console();
});

builder.Services.AddWalletTelemetry(builder.Configuration, "wallet-api");

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxConcurrentConnections = 50000;
    options.Limits.MaxConcurrentUpgradedConnections = 50000;
    options.AddServerHeader = false;
});

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
}

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<GetWalletSummaryQuery>();
    cfg.AddOpenBehavior(typeof(CachingBehavior<,>));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        context =>
        {
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "global",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 300,
                    Window = TimeSpan.FromSeconds(1),
                    QueueLimit = 100
                });
        });
});

builder.Services.AddDbContextPool<AppdbContext>(options =>
{
    options.UseNpgsql(builder.Configuration["ConnectionStrings:Postgres"]!,
        npgsql =>
        {
            npgsql.MigrationsAssembly("wallet.infrastructure");
            npgsql.EnableRetryOnFailure(3);
            npgsql.CommandTimeout(15);
        });

}, poolSize: 512);

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var configuration =
        ConfigurationOptions.Parse(
            builder.Configuration["Redis:ConnectionString"]!);

    configuration.AbortOnConnectFail = false;
    configuration.ConnectRetry = 5;
    configuration.ConnectTimeout = 5000;
    configuration.SyncTimeout = 5000;
    configuration.AsyncTimeout = 5000;
    configuration.KeepAlive = 180;
    configuration.ReconnectRetryPolicy = new ExponentialRetry(5000);

    return ConnectionMultiplexer.Connect(configuration);
})
.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"];
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
    options.SaveToken = true;
    options.RequireHttpsMetadata = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ClockSkew = TimeSpan.FromSeconds(5 * 60),
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!))
    };
    options.Events = new JwtBearerEvents
    {
        OnChallenge = context =>
        {
            Console.WriteLine("==== JWT CHALLENGE ====");
            Console.WriteLine($"ERROR: {context.Error}");
            Console.WriteLine($"ERROR DESCRIPTION: {context.ErrorDescription}");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy =
        new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
            .Build();
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:4200", "http://localhost")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()
            .WithExposedHeaders("X-Correlation-ID", "X-RateLimit-Limit", "X-RateLimit-Remaining");
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
});

builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration["ConnectionStrings:Postgres"]!,
        name: "Postgres",
        tags: new[] { "db", "postgres" })
    .AddRedis(
        builder.Configuration["Redis:ConnectionString"]!,
        name: "Redis",
        tags: new[] { "cache", "redis" })
    .AddKafka(
        setup =>
        {
            setup.BootstrapServers = builder.Configuration["Kafka:BootstrapServers"];
            setup.MessageTimeoutMs = 3000;
            setup.SocketTimeoutMs = 3000;
        },
        name: "Kafka",
        tags: new[] { "messaging", "kafka" })
    .AddUrlGroup(
        new Uri("http://otel-collector:13133/"),
        name: "otel-collector",
        tags: new[] { "telemetry", "non-critical" });

builder.Services
    .AddScoped(typeof(ICacheService<>), typeof(RedisCacheService<>));

builder.Services.AddHttpClient<IFxRateProvider,
    FrankfurterFxProvider>(client =>
{
    client.BaseAddress =
        new Uri("https://api.frankfurter.dev");
});

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ICurrencyRepository, CurrencyRepository>();
builder.Services.AddScoped<IWalletRepository, WalletRepository>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

builder.Services.Configure<KafkaSettings>(builder.Configuration.GetSection("Kafka"));
builder.Services.AddSingleton<IOutboxTrigger, OutboxTrigger>();
builder.Services.AddSingleton<IKafkaProducer, KafkaProducer>();
builder.Services.AddHostedService<OutboxProcessorService>();

// 1. Register the standard Problem Details service
builder.Services.AddProblemDetails();

// 2. Register your custom exception handler
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddOpenApi();

var app = builder.Build();

// **CRITICAL: UseForwardedHeaders MUST be first to resolve real client IP and protocol**
app.UseForwardedHeaders();

// **CRITICAL: Add proxy debugging middleware**
app.Use(async (context, next) =>
{
    context.Response.Headers.Server = string.Empty;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    await next();
});

app.MapOpenApi();
app.UseCors("AllowFrontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAppSessionContext();
app.UseExceptionHandler();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppdbContext>();
    var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
    if (pendingMigrations.Any())
    {
        await dbContext.Database.MigrateAsync();
    }
}

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new HealthResponse(report.Status.ToString(),
            report.Entries.Select(e => new HealthCheckDetail(e.Key, e.Value.Status.ToString(), e.Value.Description))
        );

        await context.Response.WriteAsJsonAsync(payload, AppJsonContext.Default.HealthResponse);
    }
});

var api = app.MapGroup("/api").RequireAuthorization();

api.MapGet("/wallet/summary", WalletApiHandlers.GetWalletSummary);

api.MapGet("/wallet/lookup/{alias}", WalletApiHandlers.LookupRecipient);

api.MapGet("/wallet/supported-currencies", WalletApiHandlers.GetSupportedCurrencies);

api.MapPost("/wallet/fx/quote", WalletApiHandlers.GetFxQuote);

api.MapPut("/wallet/update-status", WalletApiHandlers.UpdateWalletStatus);

api.MapPost("/wallet/send", WalletApiHandlers.SendMoney);

api.MapGet("/wallet/transactions", WalletApiHandlers.GetTransactions);

app.Run();

public record HealthResponse(string Status, IEnumerable<HealthCheckDetail> Checks);
public record HealthCheckDetail(string Name, string Status, string? Description);
[JsonSerializable(typeof(HealthResponse))]
internal partial class AppJsonContext : JsonSerializerContext { }
public sealed record WalletStatusRequest(string CurrencyCode);
public sealed record FxQuoteRequest(string SourceCurrency, decimal ReceivingAmount, string DestinationCurrency);
public sealed record FxQuoteResponse(decimal FinalAmount, decimal ExchangeRate, decimal TransactionFee);
public sealed record SendMoneyRequest(string ReceiverWalletId, decimal SourceAmount, string DestinationCurrency, decimal DestinationAmount, decimal FxRate, string FeeCurrency, decimal TransactionFee);