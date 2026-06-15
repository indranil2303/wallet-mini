namespace wallet.application.interfaces;
public interface IOutboxTrigger
{
    void NotifyNewMessage();
    ValueTask<bool> WaitForMessageAsync(CancellationToken cancellationToken = default!);
    bool TryConsumeMessage();
}