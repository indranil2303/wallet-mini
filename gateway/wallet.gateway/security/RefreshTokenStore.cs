using System.Text;
using System.Text.Json;
using System.Security;
using System.Security.Cryptography;
using StackExchange.Redis;

namespace wallet.gateway.security;
public sealed class RefreshTokenStore
{
    private const string Prefix = "REF_TOKEN";
    private readonly IDatabase _database;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public RefreshTokenStore(IConnectionMultiplexer redis)
        => _database = redis.GetDatabase();

    public async Task StoreAsync(string refreshToken,
        RefreshTokenRequest session,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(refreshToken);
        var expiry =
            session.ExpiresAtUtc - DateTime.UtcNow;

        if (expiry <= TimeSpan.Zero)
        {
            expiry = TimeSpan.FromMinutes(1);
        }

        var payload =
            JsonSerializer.Serialize(session, JsonOptions);

        await _database.StringSetAsync(key, payload, expiry);
    }

    public async Task<RefreshTokenRequest?> GetAsync(string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(refreshToken);

        var payload =
            await _database.StringGetAsync(key);

        if (!payload.HasValue)
        {
            return null;
        }

        return JsonSerializer.Deserialize<RefreshTokenRequest>(
            (string)payload!, JsonOptions);
    }

    public async Task<string> RotateAsync(string currentRefreshToken,
        CancellationToken cancellationToken = default)
    {
        var existing =
            await GetAsync(currentRefreshToken, cancellationToken);
        if (existing is null)
        {
            throw new SecurityException("Invalid refresh token.");
        }

        var newRefreshToken = GenerateToken();
        var updatedSession = existing with
        {
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        };

        await StoreAsync(newRefreshToken,
            updatedSession,
            cancellationToken);

        await RemoveAsync(currentRefreshToken,
            cancellationToken);

        return newRefreshToken;
    }

    public async Task RemoveAsync(string refreshToken,
        CancellationToken cancellationToken = default)
    {
        await _database.KeyDeleteAsync(
            BuildKey(refreshToken));
    }

    public async Task<bool> ExistsAsync(string refreshToken,
        CancellationToken cancellationToken = default)
    {
        return await _database.KeyExistsAsync(
            BuildKey(refreshToken));
    }

    public string GenerateToken()
    {
        Span<byte> buffer = stackalloc byte[64];
        RandomNumberGenerator.Fill(buffer);

        return Convert.ToHexString(buffer);
    }

    private static string BuildKey(string refreshToken)
    {
        return $"{Prefix}:{Hash(refreshToken)}";
    }

    private static string Hash(string token)
    {
        var hash =
            SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }
}