// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using OpenTelemetry.Metrics;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>A metrics pipeline that records which meters were subscribed instead of building a provider.</summary>
/// <remarks>
/// Hand-written for the same reason as <see cref="RecordingTracerProviderBuilder" />: the builder is an abstract class,
/// and the assertion is about the set of names that reached it.
/// </remarks>
internal sealed class RecordingMeterProviderBuilder : MeterProviderBuilder
{
    private readonly List<string> subscribedMeters = [];

    /// <summary>Gets the meter names subscribed so far, in the order they arrived.</summary>
    public IReadOnlyList<string> SubscribedMeters => this.subscribedMeters;

    /// <inheritdoc />
    public override MeterProviderBuilder AddMeter(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);

        this.subscribedMeters.AddRange(names);

        return this;
    }

    /// <inheritdoc />
    public override MeterProviderBuilder AddInstrumentation<TInstrumentation>(
        Func<TInstrumentation> instrumentationFactory)
        where TInstrumentation : class
        => this;
}
