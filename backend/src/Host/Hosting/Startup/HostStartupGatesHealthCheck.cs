// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MailFathom.Host.Hosting.Startup;

/// <summary>Reports whether the host has finished coming up, which is the one question the startup probe asks.</summary>
/// <remarks>
/// It consults process-local state only and reaches no dependency. The gates it reports on are the ones that talk to a
/// dependency, and they have already failed the host if the dependency refused them, so asking again here would turn a
/// startup probe into a second readiness probe.
/// </remarks>
internal sealed class HostStartupGatesHealthCheck : IHealthCheck
{
    /// <summary>The name the check is registered under.</summary>
    internal const string Name = "startup-gates";

    private readonly HostStartupGates startupGates;

    /// <summary>Initializes a new startup-gate health check.</summary>
    /// <param name="startupGates">The tracker the host's gates report their completion to.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="startupGates" /> is <see langword="null" />.</exception>
    public HostStartupGatesHealthCheck(HostStartupGates startupGates)
    {
        ArgumentNullException.ThrowIfNull(startupGates);

        this.startupGates = startupGates;
    }

    /// <inheritdoc />
    /// <remarks>The description names no gate. A probe response carries the aggregate status alone, and a description that named the pending step would only reach a log an operator already has the startup records in.</remarks>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(this.startupGates.Completed
            ? HealthCheckResult.Healthy("The host has completed every startup gate.")
            : HealthCheckResult.Unhealthy("The host is still completing its startup gates."));
}
