namespace wallet.domain.contracts;

public sealed record WalletSentEvent(Guid RequestId,
    EventConstants WalletEvent,
    int SenderUserId,
    string SenderCurrency,
    decimal SenderAmount,
    int ReceiverUserId,
    string ReceiverCurrency,
    decimal ReceiverAmount,
    string Message,
    DateTime CreatedAtUtc);
public enum EventConstants
{
    SUCCESS,
    FAIL
}