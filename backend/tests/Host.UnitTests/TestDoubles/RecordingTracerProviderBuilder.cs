// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using OpenTelemetry.Trace;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>A tracing pipeline that records which activity sources were subscribed instead of building a provider.</summary>
/// <remarks>
/// The builder is hand-written rather than substituted because it is an abstract class, and because what a test asserts
/// here is the set of names that reached it rather than any interaction with it.
/// </remarks>
internal sealed class RecordingTracerProviderBuilder : TracerProviderBuilder
{
    private readonly List<string> subscribedSources = [];

    /// <summary>Gets the activity source names subscribed so far, in the order they arrived.</summary>
    public IReadOnlyList<string> SubscribedSources => this.subscribedSources;

    /// <inheritdoc />
    public override TracerProviderBuilder AddSource(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);

        this.subscribedSources.AddRange(names);

        return this;
    }

    /// <inheritdoc />
    public override TracerProviderBuilder AddLegacySource(string operationName) => this;

    /// <inheritdoc />
    public override TracerProviderBuilder AddInstrumentation<TInstrumentation>(
        Func<TInstrumentation> instrumentationFactory)
        where TInstrumentation : class
        => this;
}
