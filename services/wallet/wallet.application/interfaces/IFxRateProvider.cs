namespace wallet.application.interfaces;
public interface IFxRateProvider
{
    Task<FrankfurterResponse?> GetRateAsync(string sourceCurrency, string destinationCurrency, CancellationToken cancellationToken = default);
    Task<bool> ValidateFxRate(string sourceCurrency, string destinationCurrency, decimal fxRate, CancellationToken cancellationToken = default);
}

public sealed record FrankfurterResponse(DateTime Date, string Base, string Quote, decimal Rate);