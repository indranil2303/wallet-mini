using MediatR;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using wallet.api.extensions;
using wallet.application.query;
using wallet.application.command;
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
using Microsoft.AspNetCore.Mvc;
using wallet.application.behaviour;
using wallet.domain.entities;
using wallet.domain.contracts;

var builder = WebApplication.CreateBuilder(args);

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
    cfg.RegisterServicesFromAssembly(typeof(SendMoneyCommand).Assembly);
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
            setup.MessageTimeoutMs = 5000;
        },
        name: "Kafka",
        tags: new[] { "messaging", "kafka" });

builder.Services
    .AddScoped(typeof(ICacheService<>), typeof(RedisCacheService<>));

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<GetTransactionsQuery>();
    cfg.AddOpenBehavior(typeof(CachingBehavior<,>));
});

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
builder.Services.AddSingleton<IKafkaProducer, KafkaProducer>();
builder.Services.AddHostedService<OutboxProcessorService>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

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

        //  Zero reflection serialization
        await context.Response.WriteAsJsonAsync(payload, AppJsonContext.Default.HealthResponse);
    }
});

var api = app.MapGroup("/api");

api.MapGet("/wallet/summary",
    async (HttpContext context,
    IMediator mediator) =>
{
    var _session = context.GetSession();
    var result = await mediator.Send(new GetWalletSummaryQuery(_session));
    return Results.Ok(result);
})
.RequireAuthorization();

api.MapGet("/wallet/lookup/{alias}", async (
    string alias,
    HttpContext context,
    AppdbContext dbContext,
    IConnectionMultiplexer redis,
    CancellationToken cancellationToken) =>
{
    var _session = context.GetSession();
    // 1. Sanitize the input to prevent cache duplication
    var cleanAlias = alias.Trim().ToLowerInvariant();
    var db = redis.GetDatabase();
    var cacheKey = $"lookup:alias:{cleanAlias}";

    var cachedResult = await db.StringGetAsync(cacheKey);
    if (cachedResult.HasValue)
    {
        var response = JsonSerializer.Deserialize<RecipientLookupRecord>(cachedResult.ToString());
        return Results.Ok(response);
    }

    // 3. Cache Miss: Query Postgres via highly optimized projection
    var recipient = await dbContext.Users
            .Include(x => x.WalletAccount)
            .AsNoTracking()
            .Where(w => w.Alias == cleanAlias && w.Id != _session.APP_USR_ID
                && w.IsActive)
                .Select(w => new { WalletId = w.WalletAccount.Id, FullName = w.FullName, Alias = w.Alias })
                .FirstOrDefaultAsync(cancellationToken);

    if (recipient is null)
    {
        return Results.NotFound(new { Message = $"Alias not found: {alias}" });
    }

    // 4. Mask the name (e.g., "John Doe" becomes "John D.")
    var nameParts = recipient.FullName.Split(' ');
    var maskedName = nameParts.Length > 1
        ? $"{nameParts[0]} {nameParts[1][0]}."
        : recipient.FullName;

    var lookupResponse = new RecipientLookupRecord(recipient.WalletId, maskedName, recipient.Alias);
    await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(lookupResponse),
        TimeSpan.FromMinutes(15));

    return Results.Ok(lookupResponse);
})
.RequireAuthorization();

api.MapGet("/wallet/supported-currencies",
async ([FromQuery] string? baseCurrency,
ICurrencyRepository currencyRepository) =>
{
    if (!string.IsNullOrWhiteSpace(baseCurrency))
    {
        var currency =
            await currencyRepository
                .GetCurrencyAsync(baseCurrency);

        return currency is null
            ? Results.NotFound(new
            {
                Message = $"Currency '{baseCurrency}' not found."
            })
            : Results.Ok(currency);
    }

    var currencies =
        await currencyRepository
            .GetAllCurrenciesAsync();

    return Results.Ok(currencies?
            .OrderBy(x => x.Code)
                .Select(x => new CurrencyRecord(x.Code, x.Name))
    );
})
.RequireAuthorization();

api.MapPost("/wallet/fx/quote",
    async ([FromBody] FxQuoteRequest request,
    IFxRateProvider fxProvider,
    ILogger<Program> logger) =>
    {
        var fx = await fxProvider.GetRateAsync(request.SourceCurrency, request.DestinationCurrency);
        if(fx is null)
        {
            return Results.BadRequest(new { Message = $"FX rate not available for {request.SourceCurrency} to {request.DestinationCurrency}" });
        }
        
        var convertedAmount = Math.Round(request.ReceivingAmount / fx!.Rate, 2);
        var transactionFee = Math.Round(convertedAmount * 0.01m, 2); // 1% fee for simplicity, will be configured later
        logger.LogInformation("FX Quote: {SourceCurrency} to {DestinationCurrency} - Rate: {Rate}, Converted Amount: {ConvertedAmount}, Transaction Fee: {TransactionFee}",
            request.SourceCurrency, request.DestinationCurrency, fx.Rate, convertedAmount, transactionFee);
        return Results.Ok(new FxQuoteRecord(convertedAmount, fx!.Rate, transactionFee));
    })
.RequireAuthorization();

api.MapPut("/wallet/update-status",
    async ([FromBody] WalletStatusRequest request,
    HttpContext context,
    IMediator mediator) =>
    {
        var _session = context.GetSession();
        await mediator.Send(new SaveWalletStatusCommand(_session.APP_USR_ID, request.CurrencyCode));
        return Results.NoContent();
    })
.RequireAuthorization();

api.MapPost("/wallet/send",
    async ([FromBody] SendMoneyRequest sendMoneyRequest,
    HttpContext context,
    IMediator mediator) =>
    {
        var _session = context.GetSession();
        var transactionId = await mediator.Send(new SendMoneyCommand(_session.APP_USR_ID, sendMoneyRequest.ReceiverWalletId, sendMoneyRequest.SourceAmount, sendMoneyRequest.DestinationCurrency, sendMoneyRequest.DestinationAmount, sendMoneyRequest.FxRate, sendMoneyRequest.FeeCurrency, sendMoneyRequest.TransactionFee));
        return Results.Ok(new { id = transactionId });
    })
.RequireAuthorization();

api.MapGet("/wallet/transactions", async (
    HttpContext context,
    IMediator mediator,
    [FromQuery] DateTime? startDate,
    [FromQuery] DateTime? endDate,
    [FromQuery] int pageIndex,
    [FromQuery] int pageSize) =>
    {
        var _session = context.GetSession();
        var result = await mediator.Send(new GetTransactionsQuery(_session.APP_USR_ID, startDate, endDate, pageIndex, pageSize));
        return Results.Ok(result);
    })
.RequireAuthorization();

app.Run();

public record HealthResponse(string Status, IEnumerable<HealthCheckDetail> Checks);
public record HealthCheckDetail(string Name, string Status, string? Description);
[JsonSerializable(typeof(HealthResponse))]
internal partial class AppJsonContext : JsonSerializerContext { }
public sealed record WalletStatusRequest(string CurrencyCode);
public sealed record FxQuoteRequest(string SourceCurrency, decimal ReceivingAmount, string DestinationCurrency);
public sealed record FxQuoteRecord(decimal FinalAmount, decimal ExchangeRate, decimal TransactionFee);
public sealed record SendMoneyRequest(string ReceiverWalletId, decimal SourceAmount, string DestinationCurrency, decimal DestinationAmount, decimal FxRate, string FeeCurrency, decimal TransactionFee);