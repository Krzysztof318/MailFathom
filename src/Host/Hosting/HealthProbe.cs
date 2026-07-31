// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MailFathom.Host.Hosting;

/// <summary>One of the three questions an orchestrator asks a process, and the checks that answer it.</summary>
/// <remarks>
/// <para>
/// Three probes rather than one endpoint, because the three have different consequences. A failed startup probe extends
/// the grace period a slow first start is allowed, a failed readiness probe removes the instance from traffic, and a
/// failed liveness probe kills the container. One answer wired to all three turns any of those outcomes into all of
/// them: a database outage would restart every replica of a process that is working correctly.
/// </para>
/// <para>
/// Which checks a probe consults is decided by <see cref="Tag" /> rather than by three lists maintained beside the
/// registrations. A check states its probe membership once, where it is registered, and a check that states none
/// reaches no probe — deliberately, because silently landing in all three is how a dependency check ends up able to
/// restart the process.
/// </para>
/// <para>
/// Both the path and the tag are published identities: the path is what a Kubernetes probe or a Compose health check
/// names, and the tag is what a registration writes. Neither is renamed without changing what a deployment configured.
/// </para>
/// </remarks>
/// <param name="Path">The request path this probe answers on, served only on the health-endpoint listener.</param>
/// <param name="Tag">The health-check tag that selects the checks this probe reports.</param>
internal sealed record HealthProbe(string Path, string Tag)
{
    /// <summary>Gets the probe reporting whether the host's startup gates have completed.</summary>
    /// <remarks>It answers "has this process finished coming up", which is what distinguishes a slow first start from one that is failing.</remarks>
    internal static HealthProbe Startup { get; } = new("/started", "startup");

    /// <summary>Gets the probe reporting whether the process can serve a request right now.</summary>
    /// <remarks>It consults the dependencies a request actually needs, the database among them, so an unreachable database removes the instance from traffic without restarting it.</remarks>
    internal static HealthProbe Readiness { get; } = new("/health", "ready");

    /// <summary>Gets the probe reporting whether the process is still running rather than stuck.</summary>
    /// <remarks>It consults process-local state only. A dependency outage must never reach it, because the restart it would trigger cannot fix anything outside this process and turns one outage into two.</remarks>
    internal static HealthProbe Liveness { get; } = new("/alive", "live");

    /// <summary>Gets every probe the health-endpoint listener serves.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    internal static IReadOnlyList<HealthProbe> All { get; } = [Startup, Readiness, Liveness];

    /// <summary>Reports whether a request path is one of the probe paths.</summary>
    /// <param name="path">The request path.</param>
    /// <returns><see langword="true" /> when the path is served by a probe, otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// The comparison is exact rather than by segment prefix. A probe answers one path, so treating <c>/health/x</c> as
    /// a probe path would keep a request off the application listener that no probe was ever going to answer.
    /// </remarks>
    internal static bool IsProbePath(PathString path) =>
        All.Any(probe => path.Equals(probe.Path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reports whether a registered health check belongs to this probe.</summary>
    /// <param name="registration">The health-check registration.</param>
    /// <returns><see langword="true" /> when the registration carries this probe's tag, otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="registration" /> is <see langword="null" />.</exception>
    internal bool Selects(HealthCheckRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return registration.Tags.Contains(this.Tag);
    }
}
