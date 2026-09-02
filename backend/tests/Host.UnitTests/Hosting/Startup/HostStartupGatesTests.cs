// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Hosting.Startup;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Startup;

/// <summary>Covers what the startup probe reports while the host is still coming up.</summary>
public sealed class HostStartupGatesTests
{
    [Fact]
    public void Completed_WithAGateOutstanding_ReportsTheHostStillComingUp()
    {
        // Arrange
        var gates = new HostStartupGates(HostStartupGate.SecretConfiguration, HostStartupGate.DatabaseSchema);

        // Act
        gates.MarkCompleted(HostStartupGate.SecretConfiguration);

        // Assert
        Assert.False(gates.Completed);
    }

    [Fact]
    public void Completed_WithEveryGateReported_ReportsTheHostFinishedComingUp()
    {
        // Arrange
        var gates = new HostStartupGates(HostStartupGate.SecretConfiguration, HostStartupGate.DatabaseSchema);

        // Act
        gates.MarkCompleted(HostStartupGate.SecretConfiguration);
        gates.MarkCompleted(HostStartupGate.DatabaseSchema);

        // Assert
        Assert.True(gates.Completed);
    }

    /// <summary>
    /// A gate runs once and nothing sets it back, so an orchestrator that has seen a healthy startup probe hands the
    /// process over to the liveness and readiness probes and never returns to this one.
    /// </summary>
    [Fact]
    public void Completed_AfterCompletion_StaysCompletedWhateverIsReportedAfterwards()
    {
        // Arrange
        var gates = new HostStartupGates(HostStartupGate.DatabaseSchema);
        gates.MarkCompleted(HostStartupGate.DatabaseSchema);

        // Act
        gates.MarkCompleted(HostStartupGate.DatabaseSchema);
        gates.MarkCompleted(HostStartupGate.SecretConfiguration);

        // Assert
        Assert.True(gates.Completed);
    }

    [Fact]
    public async Task CheckHealthAsync_WithAGateOutstanding_ReportsUnhealthy()
    {
        // Arrange
        var check = new HostStartupGatesHealthCheck(new HostStartupGates(HostStartupGate.DatabaseSchema));

        // Act
        var result = await check.CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WithEveryGateCompleted_ReportsHealthy()
    {
        // Arrange
        var gates = new HostStartupGates(HostStartupGate.DatabaseSchema);
        gates.MarkCompleted(HostStartupGate.DatabaseSchema);

        // Act
        var result = await new HostStartupGatesHealthCheck(gates).CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
