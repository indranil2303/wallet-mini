using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using notification.api.hubs;
using notification.api.security;
using notification.api.services;
using notification.consumer.consumers;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services
    .AddHostedService<TransactionCompletedConsumer>();

builder.Services.Configure<ForwardedHeadersOptions>(
    options =>
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
        },
        name: "kafka");

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

app.MapHealthChecks("/health");

app.MapHub<NotificationHub>("/api/hubs/notifications");

app.Run();

public record HealthResponse(string Status, IEnumerable<HealthCheckDetail> Checks);
public record HealthCheckDetail(string Name, string Status, string? Description);

[JsonSerializable(typeof(HealthResponse))]
internal partial class AppJsonContext : JsonSerializerContext { }