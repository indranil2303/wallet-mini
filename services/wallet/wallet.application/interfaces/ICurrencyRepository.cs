namespace wallet.application.interfaces;
using wallet.domain.contracts;
public interface ICurrencyRepository
{
    Task<CurrencyRecord?> GetCurrencyAsync(string currencyCode, CancellationToken cancellationToken = default!);
    Task<IReadOnlyList<CurrencyRecord>?> GetAllCurrenciesAsync(CancellationToken cancellationToken = default!);
    Task<bool> ValidateCurrencyAsync(string currencyCode, CancellationToken cancellationToken = default!);
}