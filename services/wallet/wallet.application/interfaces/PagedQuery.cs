using wallet.application.interfaces;

public abstract record PagedQuery<TResponse> : ICacheableQuery<TResponse>
where TResponse : class
{
    public abstract string CacheKey { get; }
    public TimeSpan Expiration => 
        TimeSpan.FromSeconds(30);
}