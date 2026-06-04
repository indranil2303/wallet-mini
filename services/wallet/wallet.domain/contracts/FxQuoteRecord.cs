namespace wallet.domain.contracts; 

public sealed record FxQuoteRecord(decimal SourceAmount,
    string SourceCurrency,
    decimal DestinationAmount,
    string DestinationCurrency,
    decimal FxRate,
    decimal TransactionFee);