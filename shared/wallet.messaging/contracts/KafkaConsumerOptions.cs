using Confluent.Kafka;

namespace wallet.messaging.contracts;

public class KafkaConsumerOptions
{
    public string BootstrapServers { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public AutoOffsetReset AutoOffsetReset { get; set; } = AutoOffsetReset.Earliest;
    public bool EnableAutoCommit { get; set; } = false;
    public int MaxPollIntervalMs { get; set; } = 300_000;
    public int SessionTimeoutMs { get; set; } = 10_000;
    public int FetchWaitMaxMs { get; set; } = 50;
    public int FetchMinBytes { get; set; } = 1;
    public int RetryBackoffMs { get; set; } = 5_000;
}