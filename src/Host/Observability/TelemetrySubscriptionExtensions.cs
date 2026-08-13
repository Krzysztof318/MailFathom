// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Common.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace MailFathom.Host.Observability;

/// <summary>Subscribes the activity sources and meters this process collects, whoever publishes to them.</summary>
/// <remarks>
/// <para>
/// Emitting a signal is not collecting it: an unsubscribed source produces no span however much code publishes to it,
/// and the failure is silent. That is as true of a library paying to record a histogram as it is of MailFathom's own
/// code, which is why both groups are subscribed here rather than only the first-party one.
/// </para>
/// <para>
/// What separates the two groups is who chose the name. MailFathom's own is taken from <see cref="Telemetry.Name" />
/// rather than repeated, so the host and the publishers cannot disagree about it. A library's name is the library's to
/// choose, so it is written out here as the string that library publishes under, and the unit tests assert it against
/// the library's own declaration so that a rename arriving with a package bump fails the build instead of quietly
/// emptying a dashboard.
/// </para>
/// <para>
/// Absence from these methods is a decision as much as presence is. A name registered elsewhere is deliberately not
/// repeated here — <c>Npgsql</c> is subscribed by the Aspire PostgreSQL enrichment in
/// <c>AddDatabaseHealthAndTelemetry</c>, for both its meter and its activity source — and a library that reports
/// through <c>DiagnosticSource</c> instead of an <c>ActivitySource</c> cannot be reached by a name at all, since it
/// needs a bridging instrumentation package rather than a subscription.
/// </para>
/// </remarks>
internal static class TelemetrySubscriptionExtensions
{
    /// <summary>The meter Polly's telemetry publishes every resilience event to.</summary>
    private const string PollyMeterName = "Polly";

    /// <summary>The one name the MCP SDK publishes both its spans and its instruments under.</summary>
    private const string ModelContextProtocolTelemetryName = "Experimental.ModelContextProtocol";

    /// <summary>The meter EF Core publishes its context, query, and concurrency instruments to.</summary>
    private const string EntityFrameworkCoreMeterName = "Microsoft.EntityFrameworkCore";

    /// <summary>The one name the AI telemetry decorators publish both their spans and their instruments under.</summary>
    /// <remarks>
    /// The decorators the AI boundary applies to every chat client and embedding generator it builds pass no source
    /// name, so this is the library's own default rather than a name MailFathom chose. The unit tests read it from the
    /// library's declaration and assert it against this string.
    /// </remarks>
    private const string MicrosoftExtensionsAiTelemetryName = "Experimental.Microsoft.Extensions.AI";

    /// <summary>Subscribes the activity source MailFathom publishes spans to.</summary>
    /// <param name="tracing">The tracing pipeline being composed.</param>
    /// <returns>The same builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tracing" /> is <see langword="null" />.</exception>
    public static TracerProviderBuilder AddMailFathomActivitySources(this TracerProviderBuilder tracing)
    {
        ArgumentNullException.ThrowIfNull(tracing);

        return tracing.AddSource(Telemetry.Name);
    }

    /// <summary>Subscribes the meter MailFathom publishes instruments to.</summary>
    /// <param name="metrics">The metrics pipeline being composed.</param>
    /// <returns>The same builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="metrics" /> is <see langword="null" />.</exception>
    public static MeterProviderBuilder AddMailFathomMeters(this MeterProviderBuilder metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        return metrics.AddMeter(Telemetry.Name);
    }

    /// <summary>Subscribes the activity sources the pinned libraries publish spans to under their own names.</summary>
    /// <param name="tracing">The tracing pipeline being composed.</param>
    /// <returns>The same builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tracing" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The MCP SDK spans every JSON-RPC request and every notification other than a logging one, and tags each with the
    /// protocol method, the negotiated protocol version, the transport, the session identifier, the JSON-RPC request
    /// identifier, and — for a tool call — the tool's name. Without this subscription an MCP request is a gap in the
    /// trace between the ASP.NET Core span that carried it and the database commands it issued, which reads as work
    /// that did not happen rather than as a span nobody collected.
    /// </para>
    /// <para>
    /// The AI decorators span one call against a chat model or an embedding model, from the position the AI boundary
    /// applies them: innermost, beneath the resilience and budget decorators, so a span is one attempt rather than a
    /// retried sequence. Without this subscription a slow answer is attributable to the code around the model call and
    /// not to the call, which is the one distinction the whole span exists to draw.
    /// </para>
    /// <para>
    /// EF Core is deliberately absent. It reports through <c>DiagnosticSource</c> rather than an
    /// <c>ActivitySource</c>, so no name subscribes it; reaching it means adding a bridging instrumentation package,
    /// which would then span the same database commands the <c>Npgsql</c> source already spans.
    /// </para>
    /// </remarks>
    public static TracerProviderBuilder AddLibraryActivitySources(this TracerProviderBuilder tracing)
    {
        ArgumentNullException.ThrowIfNull(tracing);

        return tracing.AddSource(ModelContextProtocolTelemetryName, MicrosoftExtensionsAiTelemetryName);
    }

    /// <summary>Subscribes the meters the pinned libraries publish instruments to under their own names.</summary>
    /// <param name="metrics">The metrics pipeline being composed.</param>
    /// <returns>The same builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="metrics" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Polly's pipelines report attempts, outcomes, timeouts, and circuit-breaker transitions; the MCP SDK reports
    /// session and per-operation durations, broken down by protocol method and, for a tool call, by tool name; EF Core
    /// reports active contexts, queries, save operations, compiled-query cache hits and misses, execution-strategy
    /// failures, and optimistic-concurrency failures — the last of which is the aggregate behind every
    /// <c>PersistenceConcurrencyConflictException</c> and is visible nowhere else.
    /// </para>
    /// <para>
    /// The AI decorators report the duration of one provider call and the tokens it consumed, which is what makes a
    /// model's latency and a model's consumption readable per operation and per model rather than only in an invoice.
    /// </para>
    /// <para>
    /// Every tag on those instruments is a bounded set: a protocol method, a transport kind, a negotiated version, one
    /// of MailFathom's three tool names, an outcome, and — on the AI instruments — the operation, the provider, the
    /// requested and answered model names, the configured endpoint's address and port, and the token type. The MCP SDK
    /// does tag a metric with a resource URI, which would be neither bounded nor free of personal data, but only for
    /// the resource methods — and MailFathom publishes tools alone, no resources and no prompts, so the tag cannot
    /// arise. A resource capability added later brings that question with it. Nothing here opens a dimension per
    /// message, per address, or per prompt, and the one thing that could — the AI decorators capturing prompts and
    /// completions — is switched off where those decorators are applied rather than left to this subscription.
    /// </para>
    /// </remarks>
    public static MeterProviderBuilder AddLibraryMeters(this MeterProviderBuilder metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        return metrics.AddMeter(
            PollyMeterName,
            ModelContextProtocolTelemetryName,
            EntityFrameworkCoreMeterName,
            MicrosoftExtensionsAiTelemetryName);
    }
}
