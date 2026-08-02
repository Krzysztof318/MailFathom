// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Mime;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MailFathom.Host.Hosting;

/// <summary>Maps the three probe paths and decides what a probe response says.</summary>
/// <remarks>
/// The endpoints carry no authentication, no authorization, no API-key check, no origin gate, and no rate limiting.
/// That is the posture rather than an omission: an orchestrator holds no credential, and a throttled probe fails, which
/// for the liveness probe means restarting a process that was answering correctly. Exposure is controlled by which
/// network the probe port is published on and by the transport it is served under, and by nothing else.
/// </remarks>
internal static class HealthProbeEndpoints
{
    /// <summary>Maps one endpoint per probe onto the routes the health-endpoint listener answers.</summary>
    /// <param name="app">The application being composed.</param>
    /// <returns>The same application instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Each endpoint states its own predicate, so a check with no tag reaches no probe. The default predicate would
    /// include every registered check in every probe, which is how a dependency check ends up able to restart the
    /// process.
    /// </remarks>
    internal static WebApplication MapHealthProbes(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        foreach (var probe in HealthProbe.All)
        {
            app.MapHealthChecks(probe.Path, new HealthCheckOptions
            {
                Predicate = probe.Selects,
                ResponseWriter = WriteAggregateStatusAsync,
            })

            // Stated rather than inferred from the absence of a policy, so a deployment that later acquires a fallback
            // authorization policy cannot close the one surface an orchestrator has no credential for.
            .AllowAnonymous();
        }

        return app;
    }

    /// <summary>Writes the aggregate status and nothing else.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="report">The report the probe's checks produced.</param>
    /// <returns>A task that completes once the status has been written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Check names, exception messages, stack traces, durations, connection strings, host names, and dependency
    /// descriptions are all absent. The endpoint's exposure is decided by which network its port is on, and a richer
    /// body would make that decision an information-disclosure decision as well: what a probe needs is the one word an
    /// orchestrator compares, and everything beyond it describes this deployment's internals to whoever can reach the
    /// port.
    /// </remarks>
    internal static Task WriteAggregateStatusAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = MediaTypeNames.Text.Plain;

        return context.Response.WriteAsync(report.Status.ToString());
    }

    /// <summary>Reports the probes no registered check answers.</summary>
    /// <param name="registrations">Every health check registered anywhere in the host.</param>
    /// <returns>One message per probe that selects no check, empty when all three are answered.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="registrations" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A probe over no checks reports healthy, because the aggregate of nothing is healthy. For readiness that is the
    /// worst possible answer: an instance that cannot reach its database would keep receiving traffic and report itself
    /// fit while doing so. Membership is decided by a tag rather than by a list, and a tag is exactly the kind of thing
    /// that stops matching without anything failing, so composition asserts the result rather than the wiring.
    /// </remarks>
    internal static IReadOnlyList<string> FindUnansweredProbes(IEnumerable<HealthCheckRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var registered = registrations.ToArray();

        return
        [
            .. HealthProbe.All
                .Where(probe => !registered.Any(probe.Selects))
                .Select(static probe => $"No registered health check carries the '{probe.Tag}' tag, so the probe served at {probe.Path} would report healthy without consulting anything."),
        ];
    }
}
