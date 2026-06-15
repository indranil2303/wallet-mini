using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using notification.api.dispatcher;
using notification.api.hubs;
using notification.api.security;
using notification.api.services;
using Serilog;
using StackExchange.Redis;
using wallet.domain.contracts;
using wallet.messaging.contracts;
using wallet.messaging.interfaces;
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

builder.Services.AddWalletTelemetry(builder.Configuration, "wallet-notification-api");

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
    options.GroupId = builder.Configuration["Kafka:GroupId"] ?? "wallet-notification-group";
    options.Topic = builder.Configuration["Kafka:Topic"] ?? "wallet.transaction.completed";
});
builder.Services.AddSingleton<IRedisBatchHandler<WalletSentEvent>, NotificationBatchHandler>();
builder.Services.AddHostedService<RedisBatchAggregatorService<WalletSentEvent>>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxConcurrentConnections = 50000;
    options.Limits.MaxConcurrentUpgradedConnections = 50000;
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.SaveToken = true;
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer =
                    builder.Configuration["Jwt:Issuer"],
                ValidAudience =
                    builder.Configuration["Jwt:Audience"],
                ClockSkew = TimeSpan.FromSeconds(5 * 60),
                IssuerSigningKey =
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!))
            };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken =
                    context.Request.Query["access_token"];
                var path =
                    context.HttpContext.Request.Path;
                if (!string.IsNullOrWhiteSpace(accessToken)
                    && path.StartsWithSegments("/api/hubs/notifications"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine(context.Exception);
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
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.GlobalLimiter =
        PartitionedRateLimiter.Create<HttpContext, string>(
            context =>
            {
                var ip =
                    context.Connection.RemoteIpAddress?.ToString()
                    ?? "global";

                return RateLimitPartition
                    .GetFixedWindowLimiter(
                        partitionKey: ip,
                        factory: _ =>
                            new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = 500,
                                Window = TimeSpan.FromSeconds(1),
                                QueueLimit = 200
                            });
            });
});

builder.Services
    .AddSignalR(options =>
    {
        options.EnableDetailedErrors = false;
        options.MaximumReceiveMessageSize = 32 * 1024;
        options.StreamBufferCapacity = 200;
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    })
    .AddStackExchangeRedis(builder.Configuration["Redis:ConnectionString"]!);

builder.Services
    .AddSingleton<IUserIdProvider, SignalRUserIdProvider>();
builder.Services
    .AddSingleton<NotificationDispatcher>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

builder.Services
    .AddHealthChecks()

    // Redis Healthcheck
    .AddRedis(
        builder.Configuration["Redis:ConnectionString"]!,
        name: "redis")

    // Kafka Healthcheck
    .AddKafka(
        setup =>
        {
            setup.BootstrapServers =
                builder.Configuration["Kafka:BootstrapServers"];
            setup.SocketTimeoutMs = 3000;
            setup.MessageTimeoutMs = 3000;
        },
        name: "kafka",
        tags: new[] { "messaging", "kafka" })

    .AddUrlGroup(
        new Uri("http://otel-collector:13133/"),
        name: "otel-collector",
        tags: new[] { "telemetry", "non-critical" });

var app = builder.Build();

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    context.Response.Headers.Server = string.Empty;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseCors("AllowFrontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = healthCheck => !healthCheck.Tags.Contains("non-critical"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new HealthResponse(report.Status.ToString(),
            report.Entries.Select(e => new HealthCheckDetail(e.Key, e.Value.Status.ToString(), e.Value.Description))
        );

        await context.Response.WriteAsJsonAsync(payload, AppJsonContext.Default.HealthResponse);
    }
});

app.MapHub<NotificationHub>("/api/hubs/notifications");

app.Run();

public record HealthResponse(string Status, IEnumerable<HealthCheckDetail> Checks);
public record HealthCheckDetail(string Name, string Status, string? Description);

[JsonSerializable(typeof(HealthResponse))]
internal partial class AppJsonContext : JsonSerializerContext { }