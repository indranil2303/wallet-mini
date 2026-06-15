using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace wallet.telemetry;

public sealed class ActivityTraceEnricher : ILogEventEnricher
{
    public static readonly ActivityTraceEnricher Instance = new();

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToHexString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToHexString()));
        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceState", activity.TraceStateString));
        }
    }
}
