namespace wallet.messaging.contracts;

public class RedisBatchOptions
{
    public int BatchSize { get; set; } = 50;
    public TimeSpan ThrottlingInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(30);
}