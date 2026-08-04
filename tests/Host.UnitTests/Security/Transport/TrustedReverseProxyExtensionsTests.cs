// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Transport;

/// <summary>Covers which requests are served under a forwarded scheme and host, and which keep their own.</summary>
/// <remarks>
/// The policy is asserted by running the platform middleware it configures rather than by reading the options back
/// alone, because what matters is the request a deployment ends up serving. A composed pipeline is not needed for
/// that: the middleware takes an <see cref="HttpContext" /> and the options this composition produced, which is
/// exactly the pair the decision is made from.
/// </remarks>
public sealed class TrustedReverseProxyExtensionsTests
{
    private const string InternalHost = "mailfathom.internal:8080";

    private static readonly IPAddress ProxyAddress = IPAddress.Parse("10.0.0.5");

    [Fact]
    public async Task AddTrustedReverseProxy_RequestFromTheNamedProxy_ServesItUnderThePublicSchemeAndHost()
    {
        // Arrange
        var request = RequestFrom(ProxyAddress);
        request.Request.Headers["X-Forwarded-Proto"] = "https";
        request.Request.Headers["X-Forwarded-Host"] = "mail.example.test";

        // Act
        await ForwardThrough(TrustingProxies("10.0.0.5"), request);

        // Assert
        Assert.Equal("https", request.Request.Scheme);
        Assert.Equal("mail.example.test", request.Request.Host.Value);
    }

    /// <summary>A container reaches this process as a peer on a bridge or pod network, which is named as a range rather than an address.</summary>
    [Fact]
    public async Task AddTrustedReverseProxy_RequestFromWithinATrustedNetwork_ServesItUnderThePublicSchemeAndHost()
    {
        // Arrange
        var request = RequestFrom(IPAddress.Parse("10.4.7.19"));
        request.Request.Headers["X-Forwarded-Proto"] = "https";
        request.Request.Headers["X-Forwarded-Host"] = "mail.example.test";

        // Act
        await ForwardThrough(TrustingProxies("10.4.0.0/16"), request);

        // Assert
        Assert.Equal("https", request.Request.Scheme);
        Assert.Equal("mail.example.test", request.Request.Host.Value);
    }

    /// <summary>Kestrel reports an IPv4 peer in IPv6 form on a dual-mode socket, which is what a container binds.</summary>
    [Fact]
    public async Task AddTrustedReverseProxy_TrustedProxyReportedAsAnIPv4MappedAddress_StillServesThePublicOrigin()
    {
        // Arrange
        var request = RequestFrom(ProxyAddress.MapToIPv6());
        request.Request.Headers["X-Forwarded-Proto"] = "https";
        request.Request.Headers["X-Forwarded-Host"] = "mail.example.test";

        // Act
        await ForwardThrough(TrustingProxies("10.0.0.5"), request);

        // Assert
        Assert.Equal("https", request.Request.Scheme);
        Assert.Equal("mail.example.test", request.Request.Host.Value);
    }

    /// <summary>
    /// The documented escape hatch, asserted so the page describing it describes something real: a prefix covering
    /// every address believes any peer at all, which is why the page states what that gives up rather than leaving it
    /// to whoever reads the parser.
    /// </summary>
    [Fact]
    public async Task AddTrustedReverseProxy_APrefixCoveringEveryAddress_BelievesAnyPeerAtAll()
    {
        // Arrange
        var request = RequestFrom(IPAddress.Parse("203.0.113.9"));
        request.Request.Headers["X-Forwarded-Proto"] = "https";
        request.Request.Headers["X-Forwarded-Host"] = "anything.example.test";

        // Act
        await ForwardThrough(TrustingProxies("0.0.0.0/0", "::/0"), request);

        // Assert
        Assert.Equal("https", request.Request.Scheme);
        Assert.Equal("anything.example.test", request.Request.Host.Value);
    }

    /// <summary>The header is a value whoever is upstream wrote, so a peer this deployment did not name writes nothing.</summary>
    [Fact]
    public async Task AddTrustedReverseProxy_RequestFromAnUntrustedPeer_KeepsTheSchemeAndHostItArrivedUnder()
    {
        // Arrange
        var request = RequestFrom(IPAddress.Parse("203.0.113.9"));
        request.Request.Headers["X-Forwarded-Proto"] = "https";
        request.Request.Headers["X-Forwarded-Host"] = "attacker.example.test";

        // Act
        await ForwardThrough(TrustingProxies("10.0.0.5"), request);

        // Assert
        Assert.Equal("http", request.Request.Scheme);
        Assert.Equal(InternalHost, request.Request.Host.Value);
    }

    /// <summary>Loopback is the framework's own default and is cleared, because on a shared host it is every process on the machine.</summary>
    [Fact]
    public async Task AddTrustedReverseProxy_RequestFromLoopbackWithoutBeingNamed_KeepsTheSchemeAndHostItArrivedUnder()
    {
        // Arrange
        var request = RequestFrom(IPAddress.Loopback);
        request.Request.Headers["X-Forwarded-Proto"] = "https";
        request.Request.Headers["X-Forwarded-Host"] = "attacker.example.test";

        // Act
        await ForwardThrough(TrustingProxies("10.0.0.5"), request);

        // Assert
        Assert.Equal("http", request.Request.Scheme);
        Assert.Equal(InternalHost, request.Request.Host.Value);
    }

    [Fact]
    public async Task AddTrustedReverseProxy_RequestCarryingNoForwardedHeader_IsLeftUnchanged()
    {
        // Arrange
        var request = RequestFrom(ProxyAddress);

        // Act
        await ForwardThrough(TrustingProxies("10.0.0.5"), request);

        // Assert
        Assert.Equal("http", request.Request.Scheme);
        Assert.Equal(InternalHost, request.Request.Host.Value);
    }

    /// <summary>Each header is read right to left, so one hop believes the value the directly connected proxy appended and nothing an earlier one claimed.</summary>
    [Fact]
    public async Task AddTrustedReverseProxy_MoreValuesThanHopsConfigured_ReadsOnlyWhatTheNearestProxyAppended()
    {
        // Arrange
        var request = RequestFrom(ProxyAddress);
        request.Request.Headers["X-Forwarded-Proto"] = "https, https";
        request.Request.Headers["X-Forwarded-Host"] = "attacker.example.test, mail.example.test";

        // Act
        await ForwardThrough(TrustingProxies("10.0.0.5"), request);

        // Assert
        Assert.Equal("https", request.Request.Scheme);
        Assert.Equal("mail.example.test", request.Request.Host.Value);
    }

    [Fact]
    public async Task AddTrustedReverseProxy_ChainAsLongAsTheConfiguredHops_ReadsTheOutermostValue()
    {
        // Arrange
        var settings = TrustingProxies("10.0.0.5");
        settings.MaximumForwardedHops = 2;
        var request = RequestFrom(ProxyAddress);
        request.Request.Headers["X-Forwarded-Proto"] = "https, http";
        request.Request.Headers["X-Forwarded-Host"] = "mail.example.test, edge.internal";

        // Act
        await ForwardThrough(settings, request);

        // Assert
        Assert.Equal("https", request.Request.Scheme);
        Assert.Equal("mail.example.test", request.Request.Host.Value);
    }

    /// <summary>A value that parses as no host at all is discarded rather than half-read, so nothing composes an origin out of part of it.</summary>
    [Theory]
    [InlineData("mail.example.test:notaport")]
    [InlineData("mail example.test")]
    [InlineData("")]
    public async Task AddTrustedReverseProxy_MalformedForwardedHost_KeepsTheHostTheRequestArrivedUnder(string forwardedHost)
    {
        // Arrange
        var request = RequestFrom(ProxyAddress);
        request.Request.Headers["X-Forwarded-Host"] = forwardedHost;

        // Act
        await ForwardThrough(TrustingProxies("10.0.0.5"), request);

        // Assert
        Assert.Equal(InternalHost, request.Request.Host.Value);
    }

    [Fact]
    public async Task AddTrustedReverseProxy_MalformedForwardedScheme_KeepsTheSchemeTheRequestArrivedUnder()
    {
        // Arrange
        var request = RequestFrom(ProxyAddress);
        request.Request.Headers["X-Forwarded-Proto"] = "ht tps";
        request.Request.Headers["X-Forwarded-Host"] = "mail.example.test";

        // Act
        await ForwardThrough(TrustingProxies("10.0.0.5"), request);

        // Assert
        Assert.Equal("http", request.Request.Scheme);
        Assert.Equal("mail.example.test", request.Request.Host.Value);
    }

    /// <summary>The client address is deliberately out of scope: nothing here partitions, limits, or logs by one.</summary>
    [Fact]
    public async Task AddTrustedReverseProxy_RequestCarryingAForwardedClientAddress_KeepsThePeerItObservedItself()
    {
        // Arrange
        var request = RequestFrom(ProxyAddress);
        request.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
        request.Request.Headers["X-Forwarded-Proto"] = "https";

        // Act
        await ForwardThrough(TrustingProxies("10.0.0.5"), request);

        // Assert
        Assert.Equal(ProxyAddress, request.Connection.RemoteIpAddress);
    }

    [Fact]
    public void AddTrustedReverseProxy_AnyConfiguration_ConsumesTheSchemeAndHostHeadersAlone()
    {
        // Arrange
        // Act
        var forwardedHeaders = ComposedOptions(TrustingProxies("10.0.0.5"));

        // Assert
        Assert.Equal(
            ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
            forwardedHeaders.ForwardedHeaders);
    }

    [Fact]
    public void AddTrustedReverseProxy_ConfiguredProxies_ReplacesTheFrameworkLoopbackDefaults()
    {
        // Arrange
        // Act
        var forwardedHeaders = ComposedOptions(TrustingProxies("10.0.0.5", "10.4.0.0/16"));

        // Assert
        Assert.Equal([ProxyAddress], forwardedHeaders.KnownProxies);
        // Both namespaces imported here publish an IPNetwork, and only the framework's own is meant.
        Assert.Equal([System.Net.IPNetwork.Parse("10.4.0.0/16")], forwardedHeaders.KnownIPNetworks);
        Assert.Equal(1, forwardedHeaders.ForwardLimit!.Value);
    }

    private static ReverseProxyOptions TrustingProxies(params string[] trustedProxies)
    {
        var settings = new ReverseProxyOptions { Enabled = true };

        foreach (var trustedProxy in trustedProxies)
        {
            settings.TrustedProxies.Add(trustedProxy);
        }

        return settings;
    }

    private static ForwardedHeadersOptions ComposedOptions(ReverseProxyOptions settings)
    {
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddTrustedReverseProxy(settings);

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    }

    private static DefaultHttpContext RequestFrom(IPAddress remoteAddress)
    {
        var context = new DefaultHttpContext();

        context.Connection.RemoteIpAddress = remoteAddress;
        context.Connection.RemotePort = 51234;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString(InternalHost);

        return context;
    }

    private static Task ForwardThrough(ReverseProxyOptions settings, HttpContext request)
    {
        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(ComposedOptions(settings)));

        return middleware.Invoke(request);
    }
}
