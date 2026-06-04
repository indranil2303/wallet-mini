namespace wallet.application.interfaces;
public interface ICacheService<T>
{
    Task<T?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}