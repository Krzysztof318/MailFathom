// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Hosting;
using MailFathom.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers the separation between the listener MCP clients reach and the listener an orchestrator probes.</summary>
/// <remarks>
/// Both directions are asserted, because the isolation is the security control this section delivers rather than a
/// tidiness rule. A probe answered on the application port reports dependency state without a credential to whoever can
/// reach the mailbox; the protocol surface answered on the probe port would be reachable from a network that was
/// published to an orchestrator precisely because nothing sensitive was expected to answer there.
/// </remarks>
public sealed class HealthEndpointIsolationTests
{
    private static readonly IReadOnlySet<int> ProbeListenerPorts = new HashSet<int> { 8081 };

    /// <summary>
    /// The trailing-slash forms are here because routing answers them: it ignores a trailing slash, so a rule that
    /// compared for exact equality would read <c>/health/</c> as an application path, let it through, and serve the
    /// aggregate dependency status unauthenticated on the listener MCP clients reach.
    /// </summary>
    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    [InlineData("/started")]
    [InlineData("/health/")]
    [InlineData("/alive/")]
    [InlineData("/started/")]
    public void ListenerServesPath_AProbePathOnTheApplicationPort_IsNotServed(string path)
    {
        // Arrange
        var requestPath = new PathString(path);

        // Act
        var served = HealthEndpointIsolation.ListenerServesPath(8080, requestPath, ProbeListenerPorts);

        // Assert
        Assert.False(served);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    [InlineData("/started")]
    [InlineData("/health/")]
    [InlineData("/alive/")]
    [InlineData("/started/")]
    public void ListenerServesPath_AProbePathOnTheProbePort_IsServed(string path)
    {
        // Arrange
        var requestPath = new PathString(path);

        // Act
        var served = HealthEndpointIsolation.ListenerServesPath(8081, requestPath, ProbeListenerPorts);

        // Assert
        Assert.True(served);
    }

    [Theory]
    [InlineData(McpEndpointRoute.Path)]
    [InlineData("/")]
    public void ListenerServesPath_AnApplicationPathOnTheProbePort_IsNotServed(string path)
    {
        // Arrange
        var requestPath = new PathString(path);

        // Act
        var served = HealthEndpointIsolation.ListenerServesPath(8081, requestPath, ProbeListenerPorts);

        // Assert
        Assert.False(served);
    }

    [Theory]
    [InlineData(McpEndpointRoute.Path)]
    [InlineData("/")]
    public void ListenerServesPath_AnApplicationPathOnTheApplicationPort_IsServed(string path)
    {
        // Arrange
        var requestPath = new PathString(path);

        // Act
        var served = HealthEndpointIsolation.ListenerServesPath(8080, requestPath, ProbeListenerPorts);

        // Assert
        Assert.True(served);
    }

    /// <summary>
    /// Serving both schemes means two probe listeners, and an operator publishing either of them expects the probes to
    /// answer on it.
    /// </summary>
    [Fact]
    public void ListenerServesPath_AProbePathOnTheSecondProbePort_IsServed()
    {
        // Arrange
        IReadOnlySet<int> bothProbePorts = new HashSet<int> { 8081, 8443 };

        // Act
        var served = HealthEndpointIsolation.ListenerServesPath(8443, new PathString("/health"), bothProbePorts);

        // Assert
        Assert.True(served);
    }

    [Fact]
    public async Task UseHealthEndpointIsolation_ARequestTheListenerDoesNotServe_IsRefusedBeforeAnythingElseRuns()
    {
        // Arrange
        var context = RequestOn(port: 8080, path: "/health");
        var reachedTheRestOfThePipeline = false;

        // Act
        await IsolationMiddleware(ProbeListenerPorts, () => reachedTheRestOfThePipeline = true)(context);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(reachedTheRestOfThePipeline);
    }

    [Fact]
    public async Task UseHealthEndpointIsolation_ARequestTheListenerServes_ReachesTheRestOfThePipeline()
    {
        // Arrange
        var context = RequestOn(port: 8081, path: "/health");
        var reachedTheRestOfThePipeline = false;

        // Act
        await IsolationMiddleware(ProbeListenerPorts, () => reachedTheRestOfThePipeline = true)(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.True(reachedTheRestOfThePipeline);
    }

    private static RequestDelegate IsolationMiddleware(IReadOnlySet<int> probeListenerPorts, Action onReached)
    {
        var pipeline = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());

        pipeline.UseHealthEndpointIsolation(probeListenerPorts);
        pipeline.Run(_ =>
        {
            onReached();

            return Task.CompletedTask;
        });

        return pipeline.Build();
    }

    private static DefaultHttpContext RequestOn(int port, string path)
    {
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = port;
        context.Request.Path = new PathString(path);

        return context;
    }
}
