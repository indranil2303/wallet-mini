using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using wallet.application.interfaces;

namespace wallet.infrastructure.persistence;
public sealed class FrankfurterFxProvider : IFxRateProvider
{
    private readonly HttpClient _httpClient;
    private readonly ICacheService<FrankfurterResponse> _cacheService;
    private readonly ILogger<FrankfurterFxProvider> _logger;

    public FrankfurterFxProvider(HttpClient httpClient,
        ICacheService<FrankfurterResponse> cacheService,
        ILogger<FrankfurterFxProvider> logger)
    {
        _httpClient = httpClient;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<FrankfurterResponse?> GetRateAsync(string sourceCurrency,
        string destinationCurrency,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceCurrency);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationCurrency);

        var source =
            sourceCurrency.Trim().ToUpperInvariant();
        var destination =
            destinationCurrency.Trim().ToUpperInvariant();

        if (source == destination)
            return null;
  
        var cachedFx =
            await _cacheService.GetAsync(GetCacheKey(source, destination));

        if (cachedFx is not null && cachedFx.Rate > 0)
            return cachedFx;

        var url = $"/v2/rate/{sourceCurrency}/{destinationCurrency}";
        var response = await _httpClient.GetFromJsonAsync<FrankfurterResponse>(url, cancellationToken);
        _logger.LogInformation("Received FX rate from Frankfurter API: {Response}", response);
        if(response is null)
        {
            return null;
        }
        await _cacheService.SetAsync(GetCacheKey(source, destination), response, TimeSpan.FromMinutes(5));

        return response;
    }

    public async Task<bool> ValidateFxRate(string sourceCurrency, string destinationCurrency, decimal fxRate, CancellationToken cancellationToken = default)
    {
        var marginOfError = 0.0001m;
        var cachedFx = await _cacheService.GetAsync(GetCacheKey(sourceCurrency, destinationCurrency));
        if (cachedFx is not null && cachedFx.Rate > 0)
        {
            return Math.Abs(cachedFx.Rate - fxRate) <= marginOfError;
        }

        var rate = await GetRateAsync(sourceCurrency, destinationCurrency, cancellationToken);
        if (rate is null)
        {
            throw new InvalidOperationException("Unable to retrieve FX rate for validation.");
        }

        return Math.Abs(rate.Rate - fxRate) <= marginOfError;
    }

    private static string GetCacheKey(string source, string destination) => $"fx:{source}:{destination}";
}