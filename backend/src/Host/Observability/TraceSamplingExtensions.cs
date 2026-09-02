// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using OpenTelemetry.Trace;

namespace MailFathom.Host.Observability;

/// <summary>States which traces this host records, and leaves the decision to the operator where one was made.</summary>
/// <remarks>
/// <para>
/// Sampling is a decision rather than a default to inherit. Recording every span is right for this process and wrong
/// for a busy one, and a deployment paying a collector per span is the case that needs the other answer — so the answer
/// is written here, and the standard variable is what changes it.
/// </para>
/// <para>
/// What this host sets when nothing else does is parent-based always-on: every trace it starts is recorded, and a trace
/// it did not start keeps the decision the caller already made. The always-on half is what a mailbox-sized workload is
/// worth — the volume is bounded by one deployment's accounts and one assistant's tool calls rather than by public
/// traffic, a folder run that doubled is attributable only if the run before it was recorded too, and export is off
/// unless an endpoint is configured, so the default costs an unconfigured deployment nothing. The parent-based half is
/// what keeps a head decision made upstream from being overturned here: the MCP surface continues a caller's trace, and
/// a caller that dropped it is not asking this process for a fragment of it.
/// </para>
/// <para>
/// The operator's half is <c>OTEL_TRACES_SAMPLER</c> and <c>OTEL_TRACES_SAMPLER_ARG</c>, which the OpenTelemetry SDK
/// reads for itself — an environment variable rather than a MailFathom configuration key, for the same reason the
/// exporter switch is one: the bootstrap pipeline that reports a start failing is built before configuration exists,
/// and a telemetry decision the two pipelines could disagree about is a decision in the wrong place.
/// <c>EnvironmentOnlySettings</c> already fails a start that writes any <c>OTEL_*</c> name into a file or an argument,
/// so the variable is the only way to reach this and no second rule is needed for it.
/// </para>
/// <para>
/// That is why the sampler below is set only when the variable is absent. The SDK ignores its own configuration when a
/// sampler was set programmatically, and reports the fact to an event source nobody is listening to — so setting one
/// unconditionally would leave an operator's <c>OTEL_TRACES_SAMPLER=parentbased_traceidratio</c> silently doing
/// nothing, which is worse than never having offered the variable at all.
/// </para>
/// </remarks>
internal static class TraceSamplingExtensions
{
    /// <summary>The standard variable naming the sampler, which the OpenTelemetry SDK reads itself.</summary>
    internal const string SamplerVariableName = "OTEL_TRACES_SAMPLER";

    /// <summary>Sets the sampler this host records traces with unless the environment already names one.</summary>
    /// <param name="tracing">The tracing pipeline being composed.</param>
    /// <param name="configuration">The configuration the standard sampler variable is read from.</param>
    /// <returns>The same builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="tracing" /> or <paramref name="configuration" /> is <see langword="null" />.
    /// </exception>
    public static TracerProviderBuilder SetDefaultSampler(
        this TracerProviderBuilder tracing,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(tracing);
        ArgumentNullException.ThrowIfNull(configuration);

        var sampler = SamplerToSet(configuration);

        return sampler is null ? tracing : tracing.SetSampler(sampler);
    }

    /// <summary>Reads which sampler this host sets, if it sets one at all.</summary>
    /// <param name="configuration">The configuration the standard sampler variable is read from.</param>
    /// <returns>
    /// The sampler to set, or <see langword="null" /> where the environment names one and the SDK is to build it.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    internal static Sampler? SamplerToSet(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return string.IsNullOrWhiteSpace(configuration[SamplerVariableName])
            ? new ParentBasedSampler(new AlwaysOnSampler())
            : null;
    }
}
