// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Resilience;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Resilience;

/// <summary>Covers the one member of the resilience boundary an adapter outside it reaches.</summary>
public sealed class OutboundOperationRunnerTests
{
    [Fact]
    public async Task RunAsync_AnOperationThatSucceeds_ReturnsItsResult()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var runner = host.Services.GetRequiredService<IOutboundOperationRunner>();

        // Act
        var result = await runner.RunAsync(
            OutboundDependency.AiProviderInvocation,
            "an-endpoint",
            _ => Task.FromResult(7),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(7, result);
    }

    /// <summary>
    /// The single-layer rule is the executor's and reaches every caller through this port too: an inner retry inside
    /// an outer one multiplies the two attempt counts against a dependency that is already struggling.
    /// </summary>
    [Fact]
    public async Task RunAsync_ReenteringOneDependencyClass_IsRefused()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var runner = host.Services.GetRequiredService<IOutboundOperationRunner>();

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
            OutboundDependency.AiProviderInvocation,
            "an-endpoint",
            outerToken => runner.RunAsync(
                OutboundDependency.AiProviderInvocation,
                "an-endpoint",
                _ => Task.FromResult(1),
                outerToken),
            TestContext.Current.CancellationToken));
    }

    /// <summary>The instance name keys a circuit and a concurrency budget, so an unnamed one would silently share both.</summary>
    [Fact]
    public async Task RunAsync_WithoutARemoteInstance_IsRefused()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var runner = host.Services.GetRequiredService<IOutboundOperationRunner>();

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(
            OutboundDependency.AiProviderInvocation,
            "   ",
            _ => Task.FromResult(1),
            TestContext.Current.CancellationToken));
    }
}
