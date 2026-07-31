// Copyright © 2026 Krzysztof Kasprowicz

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace MailMcp.Host;

/// <summary>
/// Provides the cross-cutting observability, service-discovery, HTTP-resilience, and health-check defaults
/// that the MailMcp host applies before any feature-specific composition.
/// </summary>
internal static class ServiceDefaultsExtensions
{
    /// <summary>The readiness path, which reports every registered health check.</summary>
    internal const string HealthEndpointPath = "/health";

    /// <summary>The liveness path, which reports only the checks that say the process itself is running.</summary>
    internal const string AlivenessEndpointPath = "/alive";

    /// <summary>The tag that marks a health check as answering the liveness question rather than the readiness one.</summary>
    internal const string LivenessCheckTag = "live";

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
                    .AddMeter(PollyMeterName);
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracingOptions =>
                        tracingOptions.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath))
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
    /// Adds the default liveness health check that <see cref="MapDefaultEndpoints"/> exposes.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder to configure.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), [LivenessCheckTag]);

        return builder;
    }

    /// <summary>
    /// Maps the readiness and liveness endpoints an orchestrator probes.
    /// </summary>
    /// <param name="app">The web application to configure.</param>
    /// <returns>The same application instance for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Both endpoints are mapped in every environment, because a container platform decides whether to route traffic to
    /// this process and whether to restart it by probing them, and a deployment shape MailMcp supports must not depend
    /// on the environment name it was started under. The scaffold this file came from restricted them to Development,
    /// which would have left every probe in <c>deploy/</c> with nothing to ask.
    /// </para>
    /// <para>
    /// They are deliberately left unauthenticated. Neither carries mailbox data — the response body is the single word
    /// the framework's default writer emits — and a probe has no credential to present. That is the same split
    /// <c>docs/operations/mcp-endpoint.md</c> already describes: the authorization requirement sits on the MCP route
    /// rather than on the pipeline, so these two stay reachable while everything serving mail does not.
    /// </para>
    /// <para>
    /// The two differ in what they consult, and the difference is what makes them safe to wire to different probes.
    /// <c>/health</c> runs every registered check, the database among them, so a readiness probe stops routing to a
    /// process that cannot serve. <c>/alive</c> runs only the checks tagged <c>live</c>, so a database outage never
    /// reaches a liveness probe and never turns into a restart loop that cannot fix what is actually broken.
    /// </para>
    /// </remarks>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthEndpointPath);

        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(LivenessCheckTag)
        });

        return app;
    }
}
