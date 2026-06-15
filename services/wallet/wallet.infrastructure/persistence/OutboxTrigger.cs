using System.Threading.Channels;
using wallet.application.interfaces;

namespace wallet.infrastructure.persistence;
public class OutboxTrigger : IOutboxTrigger
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) 
    { 
        FullMode = BoundedChannelFullMode.DropOldest 
    });

    // Renamed to reflect its actual behavior and returning the native ValueTask<bool>
    public ValueTask<bool> WaitForMessageAsync(CancellationToken cancellationToken = default) 
        => _channel.Reader.WaitToReadAsync(cancellationToken);

    // Fixed the syntax error by adding 'bool', using discard '_', and returning the result
    public bool TryConsumeMessage() 
        => _channel.Reader.TryRead(out bool _); 

    // Works perfectly as written
    public void NotifyNewMessage() 
        => _channel.Writer.TryWrite(true);
}