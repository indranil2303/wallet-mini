using wallet.domain.entities;

namespace wallet.domain.contracts;
public sealed record CreateTransactionRequest(Guid SenderWalletId,
    Guid ReceiverWalletId,
    string SourceCurrency,
    decimal SourceAmount,
    string DestinationCurrency,
    decimal DestinationAmount,
    decimal FxRate,
    decimal? ModifiedFxRate,
    string FeeCurrency,
    decimal TransactionFee,
    TransactionStatus Status);