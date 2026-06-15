namespace wallet.fraud_detection.contracts;
public record KafkaConfig(string BootstrapServers, string Topic, string GroupId);