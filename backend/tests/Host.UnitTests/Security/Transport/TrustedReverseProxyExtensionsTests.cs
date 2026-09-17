// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

    /// <summary>
    /// The default posture, asserted through the middleware because that is where it is felt: a deployment that
    /// configured no proxy serves any peer's forwarded scheme and host. It is the same trust as the written-out prefix
    /// above and gives up the same refusal, which is why the startup warning names it in the same terms.
    /// </summary>
    [Fact]
    public async Task AddTrustedReverseProxy_ASectionNamingNoProxy_BelievesAnyPeerAtAll()
    {
        // Arrange
        var request = RequestFrom(IPAddress.Parse("203.0.113.9"));
        request.Request.Headers["X-Forwarded-Proto"] = "https";
        request.Request.Headers["X-Forwarded-Host"] = "anything.example.test";

        // Act
        await ForwardThrough(new ReverseProxyOptions(), request);

        // Assert
        Assert.Equal("https", request.Request.Scheme);
        Assert.Equal("anything.example.test", request.Request.Host.Value);
    }

    /// <summary>An IPv6 peer is believed by the default too, which is what the second prefix is for.</summary>
    [Fact]
    public async Task AddTrustedReverseProxy_ASectionNamingNoProxyReachedOverIPv6_BelievesThatPeerAsWell()
    {
        // Arrange
        var request = RequestFrom(IPAddress.Parse("2001:db8::99"));
        request.Request.Headers["X-Forwarded-Proto"] = "https";
        request.Request.Headers["X-Forwarded-Host"] = "anything.example.test";

        // Act
        await ForwardThrough(new ReverseProxyOptions(), request);

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

    /// <summary>
    /// What the default posture costs, pinned as a test rather than left as prose. The refusal of an access token that
    /// arrived without transport encryption reads the scheme after this policy applied it, so whoever is trusted here
    /// decides whether that refusal fires. With every peer trusted, a client asserting that its own hop was encrypted
    /// has a reusable credential accepted over clear text.
    /// </summary>
    [Fact]
    public async Task AddTrustedReverseProxy_ASectionNamingNoProxy_LetsAClientsOwnClaimSatisfyTheTokenTransportRefusal()
    {
        // Arrange
        var request = RequestFrom(IPAddress.Parse("203.0.113.9"));
        request.Request.Headers["X-Forwarded-Proto"] = "https";

        // Act
        await ForwardThrough(new ReverseProxyOptions(), request);
        var authentication = MessageReceivedOn(request);
        await TransportSecurityExtensions.RefuseATokenThatArrivedWithoutTransportEncryption(authentication);

        // Assert
        Assert.Null(authentication.Result);
    }

    /// <summary>
    /// The control the assertion above needs. Naming a proxy makes the same claim from the same peer change nothing,
    /// so the token is refused — which is what proves the test above observes a real difference rather than a refusal
    /// that never fires.
    /// </summary>
    [Fact]
    public async Task AddTrustedReverseProxy_AnUntrustedPeerClaimingEncryption_StillHasItsTokenRefused()
    {
        // Arrange
        var request = RequestFrom(IPAddress.Parse("203.0.113.9"));
        request.Request.Headers["X-Forwarded-Proto"] = "https";

        // Act
        await ForwardThrough(TrustingProxies("10.0.0.5"), request);
        var authentication = MessageReceivedOn(request);
        await TransportSecurityExtensions.RefuseATokenThatArrivedWithoutTransportEncryption(authentication);

        // Assert
        Assert.NotNull(authentication.Result);
        Assert.True(authentication.Result.None);
    }

    /// <summary>A proxy the operator named is believed about the client it forwarded, which is what an administrator's network restriction and the password bound compare.</summary>
    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("::ffff:10.0.0.5")]
    public async Task AddTrustedReverseProxy_ANamedProxyForwardingAClientAddress_ServesTheRequestAsThatClient(string peer)
    {
        // Arrange
        var request = RequestFrom(IPAddress.Parse(peer));
        request.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
        request.Request.Headers["X-Forwarded-Proto"] = "https";

        // Act
        await ForwardThrough(TrustingProxies("10.0.0.5"), request);

        // Assert
        Assert.Equal(IPAddress.Parse("203.0.113.9"), request.Connection.RemoteIpAddress);
    }

    /// <summary>A client address written by a peer nobody named is the client's own claim about itself, so the peer stays what the socket observed.</summary>
    [Fact]
    public async Task AddTrustedReverseProxy_AnUnnamedPeerForwardingAClientAddress_KeepsThePeerItObservedItself()
    {
        // Arrange
        var peer = IPAddress.Parse("198.51.100.7");
        var request = RequestFrom(peer);
        request.Request.Headers["X-Forwarded-For"] = "10.1.2.3";

        // Act
        await ForwardThrough(TrustingProxies("10.0.0.5"), request);

        // Assert
        Assert.Equal(peer, request.Connection.RemoteIpAddress);
    }

    /// <summary>
    /// Believing every peer about its scheme costs a deployment nothing it had, while believing every peer about its
    /// address would let any client choose the address a restriction compares. So a section naming no proxy, or one
    /// trusting a whole family, reads the scheme and the host and never the client address.
    /// </summary>
    [Theory]
    [InlineData]
    [InlineData("0.0.0.0/0")]
    [InlineData("10.0.0.5", "::/0")]
    public async Task AddTrustedReverseProxy_ASectionTrustingEveryPeer_KeepsThePeerItObservedItself(params string[] trustedProxies)
    {
        // Arrange
        var request = RequestFrom(ProxyAddress);
        request.Request.Headers["X-Forwarded-For"] = "10.1.2.3";
        request.Request.Headers["X-Forwarded-Proto"] = "https";

        // Act
        await ForwardThrough(TrustingProxies(trustedProxies), request);

        // Assert
        Assert.Equal(ProxyAddress, request.Connection.RemoteIpAddress);
        Assert.Equal("https", request.Request.Scheme);
    }

    [Fact]
    public void AddTrustedReverseProxy_ANamedProxy_ConsumesTheClientAddressBesideTheSchemeAndHost()
    {
        // Arrange
        // Act
        var forwardedHeaders = ComposedOptions(TrustingProxies("10.0.0.5"));

        // Assert
        Assert.Equal(
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
            forwardedHeaders.ForwardedHeaders);
    }

    [Theory]
    [InlineData]
    [InlineData("0.0.0.0/0")]
    public void ForwardedHeadersFor_ASectionTrustingEveryPeer_ConsumesTheSchemeAndHostHeadersAlone(params string[] trustedProxies)
    {
        // Act
        var forwardedHeaders = TrustedReverseProxyExtensions.ForwardedHeadersFor(TrustingProxies(trustedProxies));

        // Assert
        Assert.Equal(ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost, forwardedHeaders);
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
        var settings = new ReverseProxyOptions();

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

    /// <summary>Presents the request to the bearer handler at the point the transport refusal reads its scheme.</summary>
    private static MessageReceivedContext MessageReceivedOn(HttpContext request) =>
        new(
            request,
            new AuthenticationScheme(
                TransportSurface.Mcp.OAuthSchemeNameFor("workforce"),
                displayName: null,
                typeof(JwtBearerHandler)),
            new JwtBearerOptions());

    private static Task ForwardThrough(ReverseProxyOptions settings, HttpContext request)
    {
        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(ComposedOptions(settings)));

        return middleware.Invoke(request);
    }
}
