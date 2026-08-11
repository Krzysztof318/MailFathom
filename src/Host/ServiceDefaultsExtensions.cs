// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Host.Api;
using MailFathom.Host.Hosting;
using MailFathom.Host.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace MailFathom.Host;

/// <summary>
/// Provides the cross-cutting observability, service-discovery, HTTP-resilience, and health-check defaults
/// that the MailFathom host applies before any feature-specific composition.
/// </summary>
internal static class ServiceDefaultsExtensions
{
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
    /// <remarks>
    /// The resource is configured once for all three signals, so a log record, a metric point, and a span all name the
    /// build they came from. <see cref="StampedBuildResourceExtensions" /> holds what that adds and what it leaves to
    /// the OpenTelemetry SDK.
    /// </remarks>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(ConfigureExportedLogRecords);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddStampedBuildIdentity())
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddLibraryMeters()
                    .AddMailFathomMeters();
            })
            .WithTracing(tracing =>
            {
                tracing.AddMailFathomActivitySources()
                    .AddLibraryActivitySources()
                    // A probe arrives every few seconds for the lifetime of the process and says the same thing every
                    // time, so tracing it would fill a trace store with the polling rather than with the work.
                    .AddAspNetCoreInstrumentation(tracingOptions =>
                    {
                        tracingOptions.Filter = context => !HealthProbe.IsProbePath(context.Request.Path);
                        tracingOptions.EnrichWithHttpRequest = RedactAttachmentCapability;
                    })
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

    /// <summary>States what an exported log record carries beyond its message.</summary>
    /// <param name="logging">The options the OpenTelemetry logging provider is built from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logging" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Scopes are off, and that is a redaction decision rather than a preference. The only scope anything opens here is
    /// the one ASP.NET Core's hosting middleware opens around every request, and it carries <c>RequestPath</c> — the
    /// path exactly as it arrived, captured before any middleware runs and therefore before anything could rewrite it.
    /// This process serves one route whose path *is* a credential, so with scopes included every record any request
    /// produces would carry a live attachment capability: the database command records alone put one on the exporter
    /// twice per download, at the <c>Information</c> level this deployment ships, to be kept for a log store's
    /// retention rather than for the ten minutes the link lives.
    /// </para>
    /// <para>
    /// The alternative would be to rewrite that one value on its way out, and the SDK offers nowhere to do it:
    /// <c>LogRecord</c> exposes scopes for reading and publishes no way to replace one, so rewriting means decorating
    /// the provider itself in order to preserve two values. Neither is worth keeping. Every record already carries the
    /// trace and span identifiers of the request that produced it, and that span carries the path with the capability
    /// already removed by <see cref="RedactAttachmentCapability" /> — so the correlation survives, the path survives
    /// once, and the secret survives nowhere.
    /// </para>
    /// </remarks>
    internal static void ConfigureExportedLogRecords(OpenTelemetryLoggerOptions logging)
    {
        ArgumentNullException.ThrowIfNull(logging);

        logging.IncludeFormattedMessage = true;
        logging.IncludeScopes = false;
    }

    /// <summary>Replaces the recorded path of an attachment download with its route template.</summary>
    /// <param name="activity">The server span the instrumentation started for this request.</param>
    /// <param name="request">The request being traced.</param>
    /// <remarks>
    /// <para>
    /// The download route carries a signed capability in its path, and whoever holds that capability can fetch the file
    /// it names until it expires. The instrumentation records <c>url.path</c> verbatim, so a deployment exporting traces
    /// would be shipping short-lived bearer credentials over mail to whatever stores them — which is exactly what
    /// [email content](https://github.com/Krzysztof318/MailFathom/blob/main/docs/features/email-content.md) says a link
    /// must never reach.
    /// </para>
    /// <para>
    /// The span itself is kept rather than filtered away, because a download is real traffic an operator has to be able
    /// to see: what is removed is the one segment that is a secret. The template is written in place of it so the span
    /// still says which route was served, which is what <c>http.route</c> already reports and what makes the two agree.
    /// </para>
    /// </remarks>
    internal static void RedactAttachmentCapability(Activity activity, HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Path.StartsWithSegments(EmailAttachmentDownloadEndpoint.RoutePrefix))
        {
            return;
        }

        // Setting a tag that already exists replaces it, which is what makes enrichment able to remove a value the
        // instrumentation recorded rather than only add beside it.
        activity.SetTag("url.path", $"{EmailAttachmentDownloadEndpoint.RoutePrefix}/{{capability}}");
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
