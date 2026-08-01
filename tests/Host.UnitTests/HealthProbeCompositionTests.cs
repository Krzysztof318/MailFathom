// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Host.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers which checks each probe reports, against the health-check service the host composes.</summary>
/// <remarks>
/// The predicates are asserted through a real <see cref="HealthCheckService" /> rather than by inspecting the
/// registrations, because what the three probes have to be safe about is the answer: liveness must stay healthy while a
/// dependency is not, or a database outage restarts every replica of a process that is working.
/// </remarks>
public sealed class HealthProbeCompositionTests
{
    [Fact]
    public async Task CheckHealthAsync_WithTheDatabaseUnreachable_LeavesLivenessHealthy()
    {
        // Arrange
        var healthChecks = HealthChecksWithAnUnreachableDatabase();

        // Act
        var report = await healthChecks.CheckHealthAsync(
            HealthProbe.Liveness.Selects,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Equal(["self"], report.Entries.Keys);
    }

    [Fact]
    public async Task CheckHealthAsync_WithTheDatabaseUnreachable_TurnsReadinessUnhealthy()
    {
        // Arrange
        var healthChecks = HealthChecksWithAnUnreachableDatabase();

        // Act
        var report = await healthChecks.CheckHealthAsync(
            HealthProbe.Readiness.Selects,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        Assert.Equal(["database"], report.Entries.Keys);
    }

    [Fact]
    public async Task CheckHealthAsync_WithTheStartupGatesOutstanding_ReportsOnlyTheGates()
    {
        // Arrange
        var healthChecks = HealthChecksWithAnUnreachableDatabase();

        // Act
        var report = await healthChecks.CheckHealthAsync(
            HealthProbe.Startup.Selects,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        Assert.Equal([HostStartupGatesHealthCheck.Name], report.Entries.Keys);
    }

    /// <summary>
    /// A check that states no probe membership reaches none of them. Landing in all three by default is how a
    /// dependency check ends up able to restart the process.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_AnUntaggedCheck_ReachesNoProbe()
    {
        // Arrange
        var healthChecks = HealthChecksWithAnUnreachableDatabase();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var reports = await Task.WhenAll(HealthProbe.All.Select(probe =>
            healthChecks.CheckHealthAsync(probe.Selects, cancellationToken)));

        // Assert
        Assert.All(reports, report => Assert.DoesNotContain("unclassified", report.Entries.Keys));
    }

    [Fact]
    public void FindUnansweredProbes_TheChecksTheHostRegisters_LeavesNoProbeUnanswered()
    {
        // Arrange
        HealthCheckRegistration[] registrations =
        [
            StubHealthCheck.Registration("self", HealthStatus.Healthy, HealthProbe.Liveness.Tag),
            StubHealthCheck.Registration("database", HealthStatus.Healthy, HealthProbe.Readiness.Tag),
            StubHealthCheck.Registration(HostStartupGatesHealthCheck.Name, HealthStatus.Healthy, HealthProbe.Startup.Tag),
        ];

        // Act
        var unanswered = HealthProbeEndpoints.FindUnansweredProbes(registrations);

        // Assert
        Assert.Empty(unanswered);
    }

    /// <summary>
    /// The aggregate of no checks is healthy, so a readiness probe whose tag stopped matching would keep an instance in
    /// traffic while reporting itself fit. Composition asserts the composed result rather than the wiring for exactly
    /// that reason.
    /// </summary>
    [Fact]
    public void FindUnansweredProbes_AProbeNoCheckCarriesTheTagOf_IsReported()
    {
        // Arrange
        HealthCheckRegistration[] registrations =
        [
            StubHealthCheck.Registration("self", HealthStatus.Healthy, HealthProbe.Liveness.Tag),
            StubHealthCheck.Registration(HostStartupGatesHealthCheck.Name, HealthStatus.Healthy, HealthProbe.Startup.Tag),
        ];

        // Act
        var unanswered = HealthProbeEndpoints.FindUnansweredProbes(registrations);

        // Assert
        Assert.Contains(unanswered, message => message.Contains(HealthProbe.Readiness.Path, StringComparison.Ordinal));
    }

    private static HealthCheckService HealthChecksWithAnUnreachableDatabase()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddHealthChecks()
            .Add(StubHealthCheck.Registration("self", HealthStatus.Healthy, HealthProbe.Liveness.Tag))
            .Add(StubHealthCheck.Registration("database", HealthStatus.Unhealthy, HealthProbe.Readiness.Tag))
            .Add(StubHealthCheck.Registration(HostStartupGatesHealthCheck.Name, HealthStatus.Unhealthy, HealthProbe.Startup.Tag))
            .Add(StubHealthCheck.Registration("unclassified", HealthStatus.Unhealthy));

        return services.BuildServiceProvider().GetRequiredService<HealthCheckService>();
    }
}
