using Microsoft.EntityFrameworkCore;
using wallet.application.interfaces;
using wallet.domain.contracts;

namespace wallet.infrastructure.persistence;
public sealed class CurrencyRepository(AppdbContext dbContext) : ICurrencyRepository, IDisposable
{
    private readonly AppdbContext _dbContext = dbContext;
    public async Task<CurrencyRecord?> GetCurrencyAsync(string currencyCode, CancellationToken cancellationToken = default!)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return null;
        }

        var currency = await _dbContext.Currency
            .Where(c => !string.IsNullOrEmpty(c.Code) && !string.IsNullOrEmpty(c.Name) && c.IsActive)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == currencyCode, cancellationToken);

        return currency is null ? null : new CurrencyRecord(currency.Code, currency.Name);
    }

    public async Task<IReadOnlyList<CurrencyRecord>?> GetAllCurrenciesAsync(CancellationToken cancellationToken = default!)
    {
        var currencies = await _dbContext.Currency
            .Where(c => !string.IsNullOrEmpty(c.Code) && !string.IsNullOrEmpty(c.Name) && c.IsActive)
            .AsNoTracking()
                .ToListAsync(cancellationToken);

        return currencies.Select(c => new CurrencyRecord(c.Code, c.Name)).ToList();
    }

    public async Task<bool> ValidateCurrencyAsync(string currencyCode, CancellationToken cancellationToken = default!)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return false;
        }

        return await _dbContext.Currency
            .Where(c => !string.IsNullOrEmpty(c.Code) && !string.IsNullOrEmpty(c.Name) && c.IsActive)
            .AsNoTracking()
            .AnyAsync(c => c.Code == currencyCode, cancellationToken);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}