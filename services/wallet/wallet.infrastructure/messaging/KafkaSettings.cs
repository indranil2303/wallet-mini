namespace wallet.infrastructure.messaging;

public class KafkaSettings
{
    public string BootstrapServers { get; set; } = default!;
    public string GroupId { get; set; } = default!;
}