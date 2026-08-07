// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Common.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace MailFathom.Host.Observability;

/// <summary>Subscribes the activity source and meter MailFathom publishes to under its own name.</summary>
/// <remarks>
/// Emitting a signal is not collecting it: an unsubscribed source produces no span however much code publishes to it,
/// and the failure is silent. Both methods therefore subscribe <see cref="Telemetry.Name" /> from the declaration
/// rather than repeating the string, so the host and the publishers cannot disagree about it.
/// </remarks>
internal static class TelemetrySubscriptionExtensions
{
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
}
