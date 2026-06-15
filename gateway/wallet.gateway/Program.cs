using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using wallet.application.interfaces;
using wallet.gateway.contracts;
using wallet.gateway.security;
using wallet.infrastructure.persistence;
using Yarp.ReverseProxy.Transforms;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using wallet.domain.contracts;
using System.Security.Claims;
using wallet.domain.entities;
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

builder.Services.AddWalletTelemetry(builder.Configuration, "wallet-gateway");

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxConcurrentConnections = 50000;
    options.Limits.MaxConcurrentUpgradedConnections = 50000;
    options.AddServerHeader = false;
});

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
});

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;

        // Google login challenge
        options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!))
        };
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.Name = "auth.cookie";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = 401;
            return Task.CompletedTask;
        };
    })
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;

        options.CallbackPath = "/api/signin-google";

        options.SaveTokens = true;
        // CRITICAL: Force the security cookie to survive plain HTTP localhost testing
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

        // Intercept the middleware crash and print the raw exception
        options.Events.OnRemoteFailure = context =>
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "text/plain";
            var errorMsg = $"GOOGLE MIDDLEWARE CRASHED!\n\nReason: {context.Failure?.Message}\n\nStack Trace:\n{context.Failure?.StackTrace}";

            // Stop the redirect and write the error to the screen
            context.HandleResponse();
            return context.Response.WriteAsync(errorMsg);
        };
    });

builder.Services.AddAuthorization();

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

//  Zero-Allocation Partitioning
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, System.Net.IPAddress>(
        context =>
        {
            // Fallback to Loopback safely without allocating strings
            var remoteIp = context.Connection.RemoteIpAddress ?? System.Net.IPAddress.Loopback;

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: remoteIp,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 300,
                    Window = TimeSpan.FromSeconds(1),
                    QueueLimit = 100
                });
        });
});

builder.Services
    .AddReverseProxy()
        .LoadFromConfig(
            builder.Configuration.GetSection("ReverseProxy"))
            .AddTransforms(builderContext =>
            {
                builderContext.AddRequestTransform(
                    async context =>
                    {
                        context.ProxyRequest.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString());
                        await ValueTask.CompletedTask;
                    });
            });

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
});

builder.Services.AddHealthChecks()
    .AddRedis(builder.Configuration["Redis:ConnectionString"]!, name: "Redis")
    .AddUrlGroup(new Uri("http://wallet-api:8080/health"), name: "wallet-api")
    .AddUrlGroup(new Uri("http://wallet-notification:8080/health"), name: "wallet-notification");

builder.Services.AddSingleton<JwtTokenGenerator>();
builder.Services.AddSingleton<RefreshTokenStore>();
builder.Services.AddSingleton<AppSessionCacheRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IWalletRepository, WalletRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

var app = builder.Build();

// **CRITICAL: ForwardedHeaders MUST execute first to resolve real client IP and protocol**
app.UseForwardedHeaders();

// **CRITICAL: Exception handling middleware to log proxy issues**
app.Use(async (context, next) =>
{
    context.Response.Headers.Server = string.Empty;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    await next();
});

// **CRITICAL: CORS must execute after ForwardedHeaders but before authentication**
app.UseCors("AllowFrontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseWebSockets();

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

api.MapGet("/auth/google/login",
    async (HttpContext context) =>
{
    await context.ChallengeAsync(
        GoogleDefaults.AuthenticationScheme,

        new AuthenticationProperties
        {
            RedirectUri = "/api/auth/google/callback"
        });
});

api.MapGet("/auth/google/callback",
    async (HttpContext context,
        AppdbContext dbContext,
        IUserRepository userRepository,
        IWalletRepository walletRepository,
        AppSessionCacheRepository sessionCacheRepository,
        JwtTokenGenerator jwtGenerator,
        RefreshTokenStore refreshTokenStore) =>
{
    var authenticateResult =
        await context.AuthenticateAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

    if (!authenticateResult.Succeeded)
    {
        return Results.Content($"OAuth Failed! Reason: {authenticateResult.Failure?.Message}", "text/plain");
    }

    var googleId = authenticateResult.Principal?
        .FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    var email = authenticateResult.Principal?
        .FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

    var alias = $"{email?.Substring(0, email.IndexOf('@'))}";

    var fullName = authenticateResult.Principal?
        .FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

    if (string.IsNullOrEmpty(googleId) || string.IsNullOrEmpty(email))
    {
        return Results.BadRequest("Missing required authentication information");
    }

    AppUser? user = await dbContext.Users
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.GoogleId == googleId || x.Email == email, cancellationToken: default);

    if (user is null)
    {
        user = await userRepository.CreateUserAsync(googleId, email, alias, fullName!);
        if (user is not null)
        {
            await walletRepository.CreateAsync(new WalletAccount(user.Id), cancellationToken: default);
        }
    }

    if (user is null)
    {
        return Results.BadRequest("User creation failed.");
    }

    var accessToken = jwtGenerator.GenerateAccessToken(user);
    var refreshToken = refreshTokenStore.GenerateToken();

    await sessionCacheRepository.AddAsync(new AppSession(APP_USR_ID: user.Id, GOOGLE_ID: user.GoogleId, EMAIL_ADDR: user.Email));
    await refreshTokenStore.StoreAsync(refreshToken!, new RefreshTokenRequest(UserId: user.Id, Email: user.Email, GoogleId: user.GoogleId, ExpiresAtUtc: DateTime.UtcNow.AddDays(7)));

    var isHttps = context.Request.Headers["X-Forwarded-Proto"].ToString().Equals("https", StringComparison.OrdinalIgnoreCase) || context.Request.IsHttps;
    context.Response.Cookies.Append("refreshToken",
        refreshToken!,
        new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        });

    var callbackUrl = $"{builder.Configuration["Frontend:BaseUrl"]
    ?? "http://localhost:4200"}/login/callback?access_token={accessToken}";

    Log.Information("OAuth callback redirect: {CallbackUrl}", callbackUrl);

    return Results.Redirect(callbackUrl);
});

api.MapPost("/auth/refresh",
    async (HttpContext context,
        AppdbContext dbContext,
        JwtTokenGenerator jwtGenerator,
        RefreshTokenStore refreshTokenStore,
        CancellationToken cancellationToken) =>
{
    var refreshToken = context.Request.Cookies["refreshToken"];
    if (string.IsNullOrWhiteSpace(refreshToken))
    {
        return Results.Unauthorized();
    }

    var session =
        await refreshTokenStore.GetAsync(refreshToken,
            cancellationToken);

    if (session is null || session.ExpiresAtUtc <= DateTime.UtcNow)
    {
        return Results.Unauthorized();
    }

    var user = await dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == session.UserId, cancellationToken);

    if (user is null)
    {
        return Results.Unauthorized();
    }

    var newRefreshToken =
        await refreshTokenStore.RotateAsync(refreshToken,
            cancellationToken);

    var newAccessToken =
        jwtGenerator.GenerateAccessToken(user);

    var isHttps = context.Request.Headers["X-Forwarded-Proto"].ToString()
            .Equals("https", StringComparison.OrdinalIgnoreCase) || context.Request.IsHttps;

    context.Response.Cookies.Append("refreshToken",
        newRefreshToken,
        new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        });

    return Results.Ok(new AuthResponse
    {
        AccessToken = newAccessToken
    });
});

api.MapPost("/auth/logout",
    async (HttpContext context,
    RefreshTokenStore refreshTokenStore,
    AppSessionCacheRepository sessionCacheRepository,
    CancellationToken cancellationToken) =>
{
    var refreshToken =
        context.Request.Cookies["refreshToken"];
    if (!string.IsNullOrWhiteSpace(refreshToken))
    {
        await refreshTokenStore.RemoveAsync(refreshToken, cancellationToken);
    }

    var userIdClaim =
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var googleIdClaim =
        context.User.FindFirst("google_id")?.Value;

    if (int.TryParse(userIdClaim, out var userId) && !string.IsNullOrWhiteSpace(googleIdClaim))
    {
        await sessionCacheRepository.RemoveAsync(userId, googleIdClaim);
    }
    context.Response.Cookies.Delete("refreshToken");
    return Results.NoContent();
})
.RequireAuthorization();

app.MapReverseProxy();
app.Run();

public record HealthResponse(string Status, IEnumerable<HealthCheckDetail> Checks);
public record HealthCheckDetail(string Name, string Status, string? Description);

[JsonSerializable(typeof(HealthResponse))]
internal partial class AppJsonContext : JsonSerializerContext { }