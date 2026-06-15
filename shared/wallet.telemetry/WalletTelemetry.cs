using System.Diagnostics;

namespace wallet.telemetry;

public static class WalletTelemetry
{
    public const string ActivitySourceName = "wallet-mini";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
