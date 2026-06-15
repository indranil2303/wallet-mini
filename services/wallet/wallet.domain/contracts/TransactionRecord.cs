using System.Text.Json.Serialization;

namespace wallet.domain.contracts;

[JsonSerializable(typeof(TransactionRecord))]
public record TransactionRecord(Guid id,
string type,
string senderAliasName, 
Guid senderWalletId,
string receiverAliasName, 
Guid ReceiverWalletId,
string sourceCurrency,
decimal sourceAmount,
decimal exchangeRate,
decimal modifiedExchangeRate, 
string status,
DateTime createdAtUtc);