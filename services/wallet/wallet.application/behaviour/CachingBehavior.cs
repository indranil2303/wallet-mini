using MediatR;
using wallet.application.interfaces;

namespace wallet.application.behaviour;
public sealed class CachingBehavior<TRequest, TResponse>(ICacheService<TResponse> cache)
    : IPipelineBehavior<TRequest, TResponse>
    where TResponse: class
    where TRequest : ICacheableQuery<TResponse>
{
    public async Task<TResponse> Handle(TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var cached =
            await cache.GetAsync(request.CacheKey,
                cancellationToken);

        if (cached is not null)
        {
            return cached;
        }

        var response =
            await next();
        
        await cache.SetAsync(request.CacheKey,
            response!,
            request.Expiration,
            cancellationToken);

        return response;
    }
}