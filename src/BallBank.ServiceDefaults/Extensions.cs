using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Defaults shared by every BallBank host: OpenTelemetry (logs, metrics, traces), health endpoints,
/// service discovery and a resilient <see cref="HttpClient"/>. Lives in the Microsoft.Extensions.Hosting
/// namespace so hosts pick it up with no extra using directive.
/// </summary>
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";
    private const string ApplicationInsightsConnectionStringSetting = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    // Our own instruments, e.g. how far the projection daemon trails the events.
                    .AddMeter("BallBank.*");
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName, "Wolverine")
                    .AddAspNetCoreInstrumentation(options =>
                        // Health probes are noise in traces.
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                    .AddHttpClientInstrumentation(options =>
                        // A webhook URL is a secret carried in its path (url.full would record it).
                        // Discord's calls are traced by the channel's own span instead.
                        options.FilterHttpRequestMessage = request =>
                            request.RequestUri?.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal) != true);
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    /// <summary>Builds the telemetry of a host that is never started, as the Job's are; dispose what it returns before the process exits.</summary>
    public static IDisposable StartTelemetry(this IHost host) =>
        new TelemetryFlush(
            host.Services.GetService<TracerProvider>(),
            host.Services.GetService<MeterProvider>(),
            host.Services.GetService<LoggerProvider>());

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        // The Aspire dashboard (locally) and the collector (deployed) both set OTEL_EXPORTER_OTLP_ENDPOINT.
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // One exporter per signal: UseAzureMonitorExporter attaches traces and logs only when the host starts, which the Job's never does.
        var connectionString = builder.Configuration[ApplicationInsightsConnectionStringSetting];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            void Configure(AzureMonitorExporterOptions options)
            {
                options.ConnectionString = connectionString;

                // Every trace: a league's traffic is small, and its questions are answered from one.
                options.TracesPerSecond = null;
                options.SamplingRatio = 1f;
            }

            builder.Services.AddOpenTelemetry()
                .WithTracing(tracing => tracing.AddAzureMonitorTraceExporter(Configure))
                .WithMetrics(metrics => metrics.AddAzureMonitorMetricExporter(Configure))
                .WithLogging(logging => logging.AddAzureMonitorLogExporter(Configure));
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // A liveness check that always succeeds: the process is up and can answer.
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Health endpoints are unauthenticated, so keep them off the public surface until
        // the deployment fronts them properly (see docs/architecture.md).
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for the app to be considered ready to accept traffic.
            app.MapHealthChecks(HealthEndpointPath);

            // Only checks tagged "live" must pass for the app to be considered alive.
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live"),
            });
        }

        return app;
    }

    // Starting a host is what builds the providers, and disposing it leaves OTLP logs unsent.
    private sealed class TelemetryFlush(TracerProvider? tracing, MeterProvider? metrics, LoggerProvider? logging) : IDisposable
    {
        private const int TimeoutMilliseconds = 10_000;

        public void Dispose()
        {
            tracing?.ForceFlush(TimeoutMilliseconds);
            metrics?.ForceFlush(TimeoutMilliseconds);
            logging?.ForceFlush(TimeoutMilliseconds);
        }
    }
}
