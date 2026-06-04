using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using wallet.application.interfaces;

namespace wallet.infrastructure.persistence;

public sealed class RedisCacheService<T>(IDistributedCache cache, ILogger<RedisCacheService<T>> logger)
: ICacheService<T>
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly ILogger<RedisCacheService<T>> _logger = logger;
    public async Task<T?> GetAsync(string key,
        CancellationToken cancellationToken = default)
    {
        var json =
            await cache.GetStringAsync(key,
                cancellationToken);

        return string.IsNullOrWhiteSpace(json)
            ? default
            : JsonSerializer.Deserialize<T>(json,
                JsonOptions);
    }

    public async Task SetAsync(string key,
        T value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        await cache.SetStringAsync(key,
            JsonSerializer.Serialize(value, JsonOptions),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration
            },
            cancellationToken);
    }

    public Task RemoveAsync(string key,
        CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(key,
            cancellationToken);
    }
}