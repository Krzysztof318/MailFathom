// Copyright © 2026 Krzysztof Kasprowicz

using System.Globalization;
using System.Net;

namespace MailMcp.Host.Hosting;

/// <summary>
/// Answers a container platform's health question from inside the container, by asking the running host's own health
/// endpoint over the loopback interface and reporting the answer as a process exit code.
/// </summary>
/// <remarks>
/// <para>
/// The runtime image is built on a chiseled base, which carries no shell, no package manager, and no HTTP client. That
/// is a deliberate reduction of what an attacker who reaches the container can use, and it leaves a Docker
/// <c>HEALTHCHECK</c> with nothing to execute — the instruction runs a command inside the container rather than
/// speaking to it. This is that command, and the .NET runtime the image already ships is the only thing it needs.
/// </para>
/// <para>
/// It is not the only way MailMcp's health is observable, and it is not the preferred one. Kubernetes probes the same
/// endpoints over HTTP from the kubelet and needs nothing inside the container at all, which is why the chart's probes
/// are HTTP probes; this exists for Docker and Podman, where the platform has no such reach.
/// </para>
/// <para>
/// The probe process is short-lived and deliberately separate from the host it asks about: it composes no
/// configuration, opens no database connection, resolves no secret, and starts no worker. It therefore reports what a
/// client of this container would observe rather than what the process believes about itself.
/// </para>
/// </remarks>
internal static class ContainerHealthProbe
{
    /// <summary>The first command-line argument that selects the probe instead of starting the host.</summary>
    internal const string CommandLineSwitch = "--health-probe";

    /// <summary>The environment variable naming the path to ask for, when the default is not wanted.</summary>
    /// <remarks>
    /// A deployment that wires the container's health to liveness rather than readiness points this at <c>/alive</c>,
    /// which reports only whether the process is running and never fails because the database is unreachable.
    /// </remarks>
    internal const string PathVariableName = "MAILMCP_HEALTH_PROBE_PATH";

    /// <summary>The environment variable the ASP.NET Core hosting layer takes its HTTP ports from.</summary>
    internal const string PortsVariableName = "ASPNETCORE_HTTP_PORTS";

    /// <summary>The port assumed when <see cref="PortsVariableName" /> names none.</summary>
    /// <remarks>It is what the .NET container images configure, and what the image sets explicitly beside its <c>EXPOSE</c>.</remarks>
    internal const int DefaultPort = 8080;

    /// <summary>The exit code a container platform reads as healthy.</summary>
    internal const int HealthyExitCode = 0;

    /// <summary>The exit code a container platform reads as unhealthy.</summary>
    /// <remarks>Docker treats every non-zero code except 2 as unhealthy, and reserves 2 for its own use.</remarks>
    internal const int UnhealthyExitCode = 1;

    /// <summary>How long the probe waits before treating an unanswered request as unhealthy.</summary>
    /// <remarks>
    /// Shorter than any sensible probe interval, so a stalled request is reported rather than left to overlap with the
    /// next probe. The platform's own timeout is the outer bound; this one exists so the process ends on its own.
    /// </remarks>
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The path asked for when <see cref="PathVariableName" /> names none.</summary>
    /// <remarks>
    /// Readiness rather than liveness, because the question a container platform asks through this command is whether
    /// the container can serve — which for MailMcp includes reaching its database.
    /// </remarks>
    internal const string DefaultPath = ServiceDefaultsExtensions.HealthEndpointPath;

    /// <summary>Determines whether the command line selects the probe rather than the host.</summary>
    /// <param name="commandLineArguments">The process arguments, as received.</param>
    /// <returns><see langword="true" /> when the first argument is <see cref="CommandLineSwitch" />.</returns>
    /// <remarks>
    /// Only the first position is recognized, so an argument that happens to appear later in a longer command line
    /// cannot silently turn a host start into a probe.
    /// </remarks>
    internal static bool IsRequestedBy(string[] commandLineArguments)
    {
        ArgumentNullException.ThrowIfNull(commandLineArguments);

        return commandLineArguments is [CommandLineSwitch, ..];
    }

    /// <summary>Builds the loopback address the probe asks.</summary>
    /// <param name="configuredPorts">The value of <see cref="PortsVariableName" />, or <see langword="null" />.</param>
    /// <param name="configuredPath">The value of <see cref="PathVariableName" />, or <see langword="null" />.</param>
    /// <returns>The absolute address to request.</returns>
    /// <remarks>
    /// The address is always loopback. A probe that could be pointed at another host would report that host's health
    /// as this container's, which is the one answer a health check must never give.
    /// </remarks>
    internal static Uri ResolveProbeAddress(string? configuredPorts, string? configuredPath)
    {
        var port = ReadFirstPort(configuredPorts);

        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? DefaultPath
            : "/" + configuredPath.Trim().TrimStart('/');

        return new Uri(
            string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}{path}"),
            UriKind.Absolute);
    }

    /// <summary>Asks the health endpoint and reports the answer as an exit code.</summary>
    /// <param name="probeClient">The client the request is sent through.</param>
    /// <param name="probeAddress">The address <see cref="ResolveProbeAddress" /> produced.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><see cref="HealthyExitCode" /> for a success status, and <see cref="UnhealthyExitCode" /> otherwise.</returns>
    /// <remarks>
    /// Every failure is one answer. A refused connection, a socket that never answers, a redirect, and a
    /// <c>503 Service Unavailable</c> all mean the same thing to the platform asking, and distinguishing them here
    /// would only produce exit codes it does not read. The reason is written to standard error, which is where a
    /// platform surfaces a failed probe, and carries no configuration value and no credential.
    /// </remarks>
    internal static async Task<int> ProbeAsync(
        HttpClient probeClient,
        Uri probeAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probeClient);
        ArgumentNullException.ThrowIfNull(probeAddress);

        try
        {
            using var response = await probeClient.GetAsync(probeAddress, cancellationToken);

            if (response.StatusCode is HttpStatusCode.OK)
            {
                return HealthyExitCode;
            }

            await Console.Error.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"health-probe: {probeAddress.AbsolutePath} answered {(int)response.StatusCode}."));

            return UnhealthyExitCode;
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            await Console.Error.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"health-probe: {probeAddress.AbsolutePath} could not be reached ({failure.GetType().Name})."));

            return UnhealthyExitCode;
        }
    }

    /// <summary>Reads the first port out of the semicolon-separated list ASP.NET Core accepts.</summary>
    /// <param name="configuredPorts">The configured list, or <see langword="null" />.</param>
    /// <returns>The first port that parses, or <see cref="DefaultPort" />.</returns>
    private static int ReadFirstPort(string? configuredPorts)
    {
        if (string.IsNullOrWhiteSpace(configuredPorts))
        {
            return DefaultPort;
        }

        var firstParsablePort = configuredPorts
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(candidate => int.TryParse(candidate, CultureInfo.InvariantCulture, out var port) ? port : 0)
            .FirstOrDefault(port => port is > 0 and <= 65535);

        return firstParsablePort is 0 ? DefaultPort : firstParsablePort;
    }
}
