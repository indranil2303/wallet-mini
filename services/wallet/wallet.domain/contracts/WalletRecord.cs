namespace wallet.domain.contracts;
public record WalletRecord(string currencyCode, decimal balance, string status, bool isDefaultCurrencySet);