using StackExchange.Redis;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using wallet.domain.contracts;
using wallet.infrastructure.persistence;
using System.Text.Json;

namespace wallet.api.extensions;

public sealed class AppSessionContextEnrichmentMiddleware(RequestDelegate next,
IConnectionMultiplexer redis)
{
    private const string KeyPrefix = "APP_SESSION";
    private static readonly TimeSpan SessionTtl =
        TimeSpan.FromHours(12);
    private readonly IDatabase _redisDb =
        redis.GetDatabase();

    public async Task InvokeAsync(HttpContext context,
    AppdbContext dbContext)
    {
        if (context.User.Identity is not
            { IsAuthenticated: true })
        {
            await next(context);
            return;
        }

        var userIdClaim =
            context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var googleId =
            context.User.FindFirstValue("google_id");

        if (!int.TryParse(userIdClaim, out var userId) ||
            string.IsNullOrWhiteSpace(googleId))
        {
            context.Response.StatusCode =
                StatusCodes.Status401Unauthorized;

            return;
        }

        var redisKey =
            BuildRedisKey(userId);

        AppSession? session = null;
        var cachedSession =
            await _redisDb.StringGetAsync(redisKey);

        if (cachedSession.HasValue)
        {
            try
            {
                session =
                    JsonSerializer.Deserialize<AppSession>((string)cachedSession!);
            }
            catch (JsonException)
            {
                await _redisDb.KeyDeleteAsync(redisKey);
            }
        }

        if (session is null)
        {
            var user =
                await dbContext.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.GoogleId == googleId);

            if (user is null)
            {
                context.Response.StatusCode =
                    StatusCodes.Status401Unauthorized;

                return;
            }

            session = new AppSession(APP_USR_ID: user.Id,
                GOOGLE_ID: user.GoogleId,
                EMAIL_ADDR: user.Email ?? string.Empty);

            await _redisDb.StringSetAsync(redisKey,
                JsonSerializer.Serialize(session),
                SessionTtl);
        }
        context.Items[KeyPrefix] = session;

        await next(context);
    }

    private static string BuildRedisKey(int userId)
    {
        return $"{KeyPrefix}:USER_ID:{userId}";
    }
}

public static class AppSessionContextEnrichmentExtensions
{
    public static IApplicationBuilder UseAppSessionContext(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AppSessionContextEnrichmentMiddleware>();
    }
}

public static class HttpContextExtensions
{
    private const string KeyPrefix = "APP_SESSION";
    public static AppSession GetSession(this HttpContext context)
    {
        if (context.Items.TryGetValue(KeyPrefix, out var session) && session is AppSession userSession)
        {
            return userSession;
        }

        throw new UnauthorizedAccessException("App session is missing or invalid.");
    }
}