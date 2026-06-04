using System.Text.Json.Serialization;

namespace wallet.domain.contracts;

[JsonSerializable(typeof(TransactionRecord))]
public record TransactionRecord(Guid id,
string type, 
string destinationCurrency,
decimal destinationAmount,
decimal exchangeRate,
string senderAliasName, 
Guid senderWalletId,
string receiverAliasName, 
Guid ReceiverWalletId, 
string status,
DateTime createdAtUtc);