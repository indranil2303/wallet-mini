using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace wallet.telemetry;

public sealed record OpenTelemetryOptions(string ServiceName,
    string ServiceVersion,
    string OtlpEndpoint);

public static class TelemetryExtensions
{
    public static IServiceCollection AddWalletTelemetry(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        var otelOptions = new OpenTelemetryOptions(ServiceName: serviceName,
            ServiceVersion: "1.0.0",
            OtlpEndpoint: configuration["OpenTelemetry:OtlpEndpoint"] ?? configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://otel-collector:4317"
        );

        // Globally set the propagator so trace context flows seamlessly across Kafka and HTTP boundaries
        Sdk.SetDefaultTextMapPropagator(new CompositeTextMapPropagator(new TextMapPropagator[]
        {
            new TraceContextPropagator(),
            new BaggagePropagator()
        }));

        // Shared resource builder ensures traces and metrics are perfectly correlated in Grafana
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: otelOptions.ServiceName, serviceVersion: otelOptions.ServiceVersion);

        _ = services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                _ = builder
                    .SetResourceBuilder(resourceBuilder)
                    .AddSource(WalletTelemetry.ActivitySourceName)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.EnrichWithHttpRequest = (activity, request) =>
                        {
                            activity?.SetTag("http.target", request.Path);
                            activity?.SetTag("http.host", request.Host.Value);
                        };
                        options.EnrichWithHttpResponse = (activity, response) =>
                        {
                            activity?.SetTag("http.status_code", response.StatusCode);
                        };
                    })
                    .AddHttpClientInstrumentation()
                    // Entity Framework Core instrumentation removed to avoid missing-extension errors.
                    // If EF Core instrumentation package is added (OpenTelemetry.Instrumentation.EntityFrameworkCore),
                    // re-enable with: .AddEntityFrameworkCoreInstrumentation(options => options.SetDbStatementForText = true)
                    .AddRedisInstrumentation()
                    .AddOtlpExporter(otlpOptions =>
                    {
                        otlpOptions.Endpoint = new Uri(otelOptions.OtlpEndpoint);
                    });
            })
            .WithMetrics(builder =>
            {
                builder
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation() // CRITICAL: Captures HTTP throughput and latencies
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()    // Captures GC, CPU, and Memory usage
                    .AddOtlpExporter(otlpOptions =>
                    {
                        otlpOptions.Endpoint = new Uri(otelOptions.OtlpEndpoint);
                    });
            });
        services.AddSingleton(WalletTelemetry.ActivitySource);
        
        return services;
    }

    #region Kafka Distributed Context Propagation

    public static void InjectKafkaTraceContext(Message<string, string> message)
    {
        message.Headers ??= new Headers();

        var propagationContext = new PropagationContext(Activity.Current?.Context ?? default, Baggage.Current);
        Propagators.DefaultTextMapPropagator.Inject(propagationContext, message.Headers, InjectHeader);
    }

    public static PropagationContext ExtractKafkaTraceContext(Headers? headers)
    {
        if (headers is null)
        {
            return default;
        }

        return Propagators.DefaultTextMapPropagator.Extract(default, headers, GetHeaderValues);
    }

    public static Dictionary<string, string> ExtractHeadersFromKafkaHeaders(Headers? headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headers is null)
        {
            return result;
        }

        foreach (var header in headers)
        {
            if (header.GetValueBytes() is { } value)
            {
                result[header.Key] = Encoding.UTF8.GetString(value);
            }
        }

        return result;
    }

    private static IEnumerable<string> GetHeaderValues(Headers headers, string name)
    {
        foreach (var header in headers)
        {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase) && header.GetValueBytes() is { } value)
            {
                yield return Encoding.UTF8.GetString(value);
            }
        }
    }

    private static void InjectHeader(Headers headers, string name, string value)
    {
        headers.Remove(name);
        headers.Add(name, Encoding.UTF8.GetBytes(value));
    }

    public static PropagationContext ExtractTraceContextFromDictionary(Dictionary<string, string> traceHeaders)
    {
        return Propagators.DefaultTextMapPropagator.Extract(default, traceHeaders, GetHeaderValuesFromDictionary);
    }

    private static IEnumerable<string> GetHeaderValuesFromDictionary(Dictionary<string, string> traceHeaders, string name)
    {
        if (traceHeaders.TryGetValue(name, out var value))
        {
            return new[] { value };
        }

        return Array.Empty<string>();
    }

    #endregion
}