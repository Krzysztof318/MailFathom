// Copyright © 2026 Krzysztof Kasprowicz

using System.Net;
using MailMcp.Host.Hosting;
using MailMcp.TestSupport;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>
/// Covers the container health probe: which command lines select it, which address it asks, and which answers a
/// container platform is told mean healthy.
/// </summary>
public sealed class ContainerHealthProbeTests
{
    [Fact]
    public void IsRequestedBy_CommandLineLeadingWithTheSwitch_SelectsTheProbe()
    {
        // Arrange
        string[] commandLineArguments = ["--health-probe"];

        // Act
        var isRequested = ContainerHealthProbe.IsRequestedBy(commandLineArguments);

        // Assert
        Assert.True(isRequested);
    }

    [Fact]
    public void IsRequestedBy_SwitchAfterAnotherArgument_StartsTheHost()
    {
        // A host started with configuration overrides must never be turned into a probe by an argument that happens to
        // appear later in its command line.

        // Arrange
        string[] commandLineArguments = ["--MailboxSearch:SnippetsPerEmail=3", "--health-probe"];

        // Act
        var isRequested = ContainerHealthProbe.IsRequestedBy(commandLineArguments);

        // Assert
        Assert.False(isRequested);
    }

    [Fact]
    public void IsRequestedBy_NoArguments_StartsTheHost()
    {
        // Arrange
        string[] commandLineArguments = [];

        // Act
        var isRequested = ContainerHealthProbe.IsRequestedBy(commandLineArguments);

        // Assert
        Assert.False(isRequested);
    }

    [Fact]
    public void ResolveProbeAddress_NothingConfigured_AsksLoopbackReadinessOnTheImagePort()
    {
        // Act
        var probeAddress = ContainerHealthProbe.ResolveProbeAddress(configuredPorts: null, configuredPath: null);

        // Assert
        Assert.Equal("http://127.0.0.1:8080/health", probeAddress.AbsoluteUri);
    }

    [Theory]
    [InlineData("5000", "http://127.0.0.1:5000/health")]
    [InlineData(" 5000 ", "http://127.0.0.1:5000/health")]
    [InlineData("5000;5001", "http://127.0.0.1:5000/health")]
    public void ResolveProbeAddress_ConfiguredPorts_AsksTheFirstOne(string configuredPorts, string expectedAddress)
    {
        // ASPNETCORE_HTTP_PORTS accepts a semicolon-separated list, and a probe has to pick one; the first is the one
        // the deployment named first.

        // Act
        var probeAddress = ContainerHealthProbe.ResolveProbeAddress(configuredPorts, configuredPath: null);

        // Assert
        Assert.Equal(expectedAddress, probeAddress.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-port")]
    [InlineData("0")]
    [InlineData("70000")]
    public void ResolveProbeAddress_UnusablePorts_FallBackToTheImagePort(string configuredPorts)
    {
        // Act
        var probeAddress = ContainerHealthProbe.ResolveProbeAddress(configuredPorts, configuredPath: null);

        // Assert
        Assert.Equal("http://127.0.0.1:8080/health", probeAddress.AbsoluteUri);
    }

    [Theory]
    [InlineData("/alive", "http://127.0.0.1:8080/alive")]
    [InlineData("alive", "http://127.0.0.1:8080/alive")]
    [InlineData(" /alive ", "http://127.0.0.1:8080/alive")]
    public void ResolveProbeAddress_ConfiguredPath_AsksThatPath(string configuredPath, string expectedAddress)
    {
        // A deployment wiring the container's health to liveness points the probe at /alive, which never fails because
        // the database is unreachable.

        // Act
        var probeAddress = ContainerHealthProbe.ResolveProbeAddress(configuredPorts: null, configuredPath);

        // Assert
        Assert.Equal(expectedAddress, probeAddress.AbsoluteUri);
    }

    [Fact]
    public async Task ProbeAsync_HealthyResponse_ReportsHealthy()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => new HttpResponseMessage(HttpStatusCode.OK));
        using var probeClient = new HttpClient(handler, disposeHandler: false);

        // Act
        var exitCode = await ProbeAsync(probeClient);

        // Assert
        Assert.Equal(ContainerHealthProbe.HealthyExitCode, exitCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task ProbeAsync_AnythingOtherThanOk_ReportsUnhealthy(HttpStatusCode answeredStatus)
    {
        // A degraded health report answers 503, and every other status means the endpoint is not the one a healthy
        // MailMcp serves. The platform reads one bit either way.

        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => new HttpResponseMessage(answeredStatus));
        using var probeClient = new HttpClient(handler, disposeHandler: false);

        // Act
        var exitCode = await ProbeAsync(probeClient);

        // Assert
        Assert.Equal(ContainerHealthProbe.UnhealthyExitCode, exitCode);
    }

    [Fact]
    public async Task ProbeAsync_TransportFailure_ReportsUnhealthy()
    {
        // A container whose host has not started listening yet refuses the connection, which is the ordinary state
        // during startup and must be reported rather than raised.

        // Arrange
        using var handler = new FakeHttpMessageHandler(
            (_, _) => throw new HttpRequestException("The connection was refused."));
        using var probeClient = new HttpClient(handler, disposeHandler: false);

        // Act
        var exitCode = await ProbeAsync(probeClient);

        // Assert
        Assert.Equal(ContainerHealthProbe.UnhealthyExitCode, exitCode);
    }

    [Fact]
    public async Task ProbeAsync_RequestThatNeverAnswers_ReportsUnhealthy()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromCanceled<HttpResponseMessage>(new CancellationToken(canceled: true)));
        using var probeClient = new HttpClient(handler, disposeHandler: false);

        // Act
        var exitCode = await ProbeAsync(probeClient);

        // Assert
        Assert.Equal(ContainerHealthProbe.UnhealthyExitCode, exitCode);
    }

    private static Task<int> ProbeAsync(HttpClient probeClient) => ContainerHealthProbe.ProbeAsync(
        probeClient,
        ContainerHealthProbe.ResolveProbeAddress(configuredPorts: null, configuredPath: null),
        TestContext.Current.CancellationToken);
}
