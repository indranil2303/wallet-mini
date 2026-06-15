using Microsoft.ML.Data;
public sealed class FraudModelInput
{
    [ColumnName("type")] public string? Type { get; set; }
    [ColumnName("step")] public float Step { get; set; }
    [ColumnName("amount")] public float Amount { get; set; }
    [ColumnName("oldbalanceOrg")] public float OldBalanceOrg { get; set; }
    [ColumnName("newbalanceOrig")] public float NewBalanceOrig { get; set; }
    [ColumnName("oldbalanceDest")] public float OldBalanceDest { get; set; }
    [ColumnName("newbalanceDest")] public float NewBalanceDest { get; set; }
    [ColumnName("nameOrig")] public string? NameOrig { get; set; }
    [ColumnName("nameDest")] public string? NameDest { get; set; }
    [ColumnName("isFlaggedFraud")] public float IsFlaggedFraud { get; set;}
    [ColumnName("isFraud")] public float IsFraud {get; set;}
};