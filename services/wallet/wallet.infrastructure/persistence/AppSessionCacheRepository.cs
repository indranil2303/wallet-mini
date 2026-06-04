using System.Text.Json;
using StackExchange.Redis;
using wallet.domain.contracts;

namespace wallet.infrastructure.persistence;
public sealed class AppSessionCacheRepository
{
    private const string KeyPrefix = "APP_SESSION";
    private static readonly TimeSpan DefaultExpiry =
        TimeSpan.FromHours(12);
    private readonly IDatabase _db;

    public AppSessionCacheRepository(IConnectionMultiplexer redis)
    {
        ArgumentNullException.ThrowIfNull(redis);
        _db = redis.GetDatabase();
    }

    public async Task AddAsync(AppSession session,
    TimeSpan? expiry = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        var payload =
            JsonSerializer.Serialize(session!);

        await _db.StringSetAsync(BuildKey(session.APP_USR_ID),
            payload,
            expiry ?? DefaultExpiry);
    }

    public async Task<AppSession?> GetAsync(int appUserId,
    string googleId)
    {
        var payload =
            await _db.StringGetAsync(BuildKey(appUserId));
        if (!payload.HasValue)
        {
            return null;
        }

        return JsonSerializer.Deserialize<AppSession>((string)payload!);
    }

    public async Task<bool> RemoveAsync(int appUserId,
    string googleId)
    {
        return await _db.KeyDeleteAsync(BuildKey(appUserId));
    }

    private string BuildKey(int id)
    {
        return $"{KeyPrefix}:USER_ID:{id}";
    }
}