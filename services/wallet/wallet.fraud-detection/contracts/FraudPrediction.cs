using Microsoft.ML.Data;
namespace wallet.fraud_detection.contracts;
public sealed class FraudPrediction
{
    [ColumnName("PredictedLabel")] public float RawPredictedLabel { get; set; }
    public bool IsFraud => RawPredictedLabel > 0.5f;
    [ColumnName("Score")]
    public float[]? Score { get; init; }
};