// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Hosting;
using MailFathom.Host.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace MailFathom.Host;

/// <summary>
/// Provides the cross-cutting observability, service-discovery, HTTP-resilience, and health-check defaults
/// that the MailFathom host applies before any feature-specific composition.
/// </summary>
internal static class ServiceDefaultsExtensions
{
    /// <summary>The meter Polly's telemetry publishes every resilience event to.</summary>
    private const string PollyMeterName = "Polly";

    /// <summary>
    /// Adds observability, service discovery, HTTP resilience, and health-check defaults.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder to configure.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(httpClientBuilder =>
        {
            httpClientBuilder.AddStandardResilienceHandler();
            httpClientBuilder.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// Adds OpenTelemetry logging, metrics, tracing, and configured exporters.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder to configure.</param>
    /// <returns>The same builder instance for chaining.</returns>
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
                    // The outbound resilience pipelines report attempts, outcomes, and durations to Polly's meter.
                    // Emitting them is not exporting them: without this subscription the instruments exist and
                    // nothing collects them.
                    .AddMeter(PollyMeterName)
                    .AddMailFathomMeters();
            })
            .WithTracing(tracing =>
            {
                tracing.AddMailFathomActivitySources()
                    // A probe arrives every few seconds for the lifetime of the process and says the same thing every
                    // time, so tracing it would fill a trace store with the polling rather than with the work.
                    .AddAspNetCoreInstrumentation(tracingOptions =>
                        tracingOptions.Filter = context => !HealthProbe.IsProbePath(context.Request.Path))
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>
    /// Adds the check that reports the process itself, which is what the liveness probe consults.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder to configure.</param>
    /// <returns>The same builder instance for chaining.</returns>
    /// <remarks>
    /// It carries the liveness tag alone. Answering means the process is running its own request pipeline, which is the
    /// whole of the liveness question; adding it to readiness would say nothing a readiness probe acts on, and adding a
    /// dependency to liveness would let an outage elsewhere restart a process that is working.
    /// </remarks>
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", static () => HealthCheckResult.Healthy(), [HealthProbe.Liveness.Tag]);

        return builder;
    }

}
