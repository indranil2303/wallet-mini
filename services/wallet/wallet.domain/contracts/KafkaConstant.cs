namespace wallet.domain.messaging;
public static class KafkaConstant
{
    public const string WALLET_CREATED = "wallet.created";
    public const string WALLET_MONEY_SENT = "wallet.money.sent";
    public const string WALLET_TRANSACTION_REQUESTED = "wallet.transaction.requested";
    public const string WALLET_TRANSACTION_COMPLETED =
        "wallet.transaction.completed";
    public const string DEAD_LETTER_QUEUE =
        "wallet.deadletter";
}