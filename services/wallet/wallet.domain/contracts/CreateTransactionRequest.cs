namespace wallet.domain.contracts;
public sealed record CreateTransactionRequest(Guid SenderWalletId,
    Guid ReceiverWalletId,
    string SourceCurrency,
    decimal SourceAmount,
    string DestinationCurrency,
    decimal DestinationAmount,
    decimal FxRate,
    string FeeCurrency,
    decimal TransactionFee);