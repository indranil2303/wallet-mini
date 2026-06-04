namespace notification.consumer.contracts;
public sealed record TransactionEvent(Guid RequestId, 
    int SenderUserId,
    string SenderCurrency,
    decimal SenderAmount,
    int ReceiverUserId,
    string ReceiverCurrency,
    decimal ReceiverAmount,
    string Message, 
    DateTime CreatedAtUtc);