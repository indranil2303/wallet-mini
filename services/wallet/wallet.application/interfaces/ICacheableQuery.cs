using MediatR;

namespace wallet.application.interfaces;
public interface ICacheableQuery<T> :IRequest<T>
where T : class
{
    string CacheKey { get; }
    TimeSpan Expiration { get; }
}