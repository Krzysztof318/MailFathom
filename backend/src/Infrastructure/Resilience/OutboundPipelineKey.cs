// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Resilience;

namespace MailFathom.Infrastructure.Resilience;

/// <summary>Names one resilience pipeline: a dependency class and the remote instance whose health that pipeline tracks.</summary>
/// <param name="Dependency">The dependency class whose configured budget the pipeline applies.</param>
/// <param name="DependencyInstance">The remote instance the operation talks to, for example one configured mail account.</param>
/// <remarks>
/// <para>
/// A circuit breaker is state, and state shared between two remote servers reports neither of them. One unreachable
/// mail account would open the breaker every healthy account then runs into, so the pipeline of a dependency class
/// that talks to more than one remote instance is resolved per instance. The registry caches one pipeline per key and
/// builds it from the single builder registered for the dependency class, so isolating an instance costs configuration
/// nothing.
/// </para>
/// <para>
/// A dependency class that talks to exactly one remote instance — the local database, today's single provider
/// endpoint — uses <see cref="SharedInstance" /> and keeps one process-wide pipeline.
/// </para>
/// </remarks>
internal readonly record struct OutboundPipelineKey(OutboundDependency Dependency, string DependencyInstance)
{
    /// <summary>The instance name of a dependency class whose pipeline state is shared by every call to it.</summary>
    internal const string SharedInstance = "shared";

    /// <summary>Initializes a key for a dependency class with one process-wide pipeline.</summary>
    /// <param name="dependency">The dependency class whose configured budget the pipeline applies.</param>
    internal OutboundPipelineKey(OutboundDependency dependency)
        : this(dependency, SharedInstance)
    {
    }
}

/// <summary>Matches two pipeline keys by dependency class alone, which is what selects the builder an instance is created from.</summary>
/// <remarks>
/// The registry looks a builder up with this comparer and caches the built pipeline under the whole key. One
/// registration per dependency class therefore serves every instance of it, while each instance still gets its own
/// circuit-breaker, concurrency, and telemetry state.
/// </remarks>
internal sealed class OutboundPipelineBuilderComparer : IEqualityComparer<OutboundPipelineKey>
{
    /// <inheritdoc />
    public bool Equals(OutboundPipelineKey x, OutboundPipelineKey y) => x.Dependency == y.Dependency;

    /// <inheritdoc />
    public int GetHashCode(OutboundPipelineKey obj) => obj.Dependency.GetHashCode();
}
