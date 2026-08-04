// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Endpoints;

/// <summary>Covers which HTTPS profiles a deployment may serve, and which are refused before a listener opens.</summary>
/// <remarks>
/// Every rule here is one whose absence produces a working-looking deployment serving something an operator did not
/// configure: an unreachable domain, a version they did not select, or a certificate presented for a name that belongs
/// to their other endpoint. The QUIC capability is a parameter rather than a machine property so both answers are
/// stated here rather than depending on the host the suite happens to run on.
/// </remarks>
public sealed class TransportHttpsOptionsTests
{
    private const string SectionPath = "McpEndpoint:Https";

    /// <summary>The port a surface would redirect on, stated here so no rule below depends on which surface owns the section.</summary>
    /// <remarks>Deliberately not the port <see cref="Profile" /> binds, so a well-formed profile beside an enabled redirect stays well formed and the collision rule has to be provoked to fire.</remarks>
    private const int DefaultRedirectPort = 8080;

    [Fact]
    public void TerminatesTls_NoProfiles_IsTheClearTextPosture()
    {
        // Arrange, Act
        var options = new TransportHttpsOptions();

        // Assert
        Assert.False(options.TerminatesTls);
        Assert.Empty(options.FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));
    }

    [Fact]
    public void FindConfigurationErrors_AWellFormedProfile_ReportsNothing()
    {
        // Arrange
        var options = With(Profile());

        // Act, Assert
        Assert.True(options.TerminatesTls);
        Assert.Empty(options.FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));
    }

    [Fact]
    public void FindConfigurationErrors_AProfileWithoutAName_IsRefused()
    {
        // Arrange
        var profile = Profile();
        profile.Name = "   ";

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.StartsWith($"{SectionPath}:Endpoints:0:Name", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_AProfileWithoutADomain_IsRefused()
    {
        // Arrange
        var profile = Profile();
        profile.Domain = string.Empty;

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.StartsWith($"{SectionPath}:Endpoints:0:Domain", error, StringComparison.Ordinal);
    }

    /// <summary>A client sends no server name for an address, so a profile published under one could never be selected.</summary>
    [Theory]
    [InlineData("192.0.2.10")]
    [InlineData("2001:db8::1")]
    public void FindConfigurationErrors_ADomainThatIsAnIpAddress_IsRefused(string domain)
    {
        // Arrange
        var profile = Profile();
        profile.Domain = domain;

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.Contains("BindAddress", error, StringComparison.Ordinal);
    }

    /// <summary>Wildcard and catch-all acceptance is exactly what this section refuses to enable without being asked.</summary>
    [Theory]
    [InlineData("*.example.test")]
    [InlineData("*")]
    [InlineData("not a domain")]
    public void FindConfigurationErrors_ADomainThatIsNotAnExactDnsName_IsRefused(string domain)
    {
        // Arrange
        var profile = Profile();
        profile.Domain = domain;

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.StartsWith($"{SectionPath}:Endpoints:0:Domain", error, StringComparison.Ordinal);
    }

    /// <summary>A Unicode domain would fail its certificate's name check instead, which reads as the wrong certificate rather than the wrong spelling.</summary>
    [Fact]
    public void FindConfigurationErrors_AnInternationalizedDomain_AsksForItsPunycodeForm()
    {
        // Arrange
        var profile = Profile();
        profile.Domain = "poczta.wróbel.test";

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.Contains("punycode", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void FindConfigurationErrors_APortOutsideTheTcpRange_IsRefused(int port)
    {
        // Arrange
        var profile = Profile();
        profile.Port = port;

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.StartsWith($"{SectionPath}:Endpoints:0:Port", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_ABindAddressThatIsNotAnIpAddress_IsRefused()
    {
        // Arrange
        var profile = Profile();
        profile.BindAddress = "localhost";

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.StartsWith($"{SectionPath}:Endpoints:0:BindAddress", error, StringComparison.Ordinal);
    }

    /// <summary>Absent means the default; empty is an operator saying the profile serves nothing, which is never what they meant.</summary>
    [Fact]
    public void FindConfigurationErrors_AnEmptyHttpProtocolList_IsRefused()
    {
        // Arrange
        var profile = Profile();
        profile.HttpProtocols = [];

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.StartsWith($"{SectionPath}:Endpoints:0:HttpProtocols", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ServedHttpProtocols_NoneConfigured_IsHttp11AndHttp2()
    {
        // Arrange, Act
        var served = Profile().ServedHttpProtocols;

        // Assert
        Assert.Equal([TransportHttpProtocol.Http1, TransportHttpProtocol.Http2], served);
    }

    [Fact]
    public void FindConfigurationErrors_AnHttpVersionListedTwice_IsRefused()
    {
        // Arrange
        var profile = Profile();
        profile.HttpProtocols = [TransportHttpProtocol.Http1, TransportHttpProtocol.Http1];

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.StartsWith($"{SectionPath}:Endpoints:0:HttpProtocols", error, StringComparison.Ordinal);
    }

    /// <summary>Falling back would serve HTTP/2 to an operator who read a working endpoint and believed it was HTTP/3.</summary>
    [Fact]
    public void FindConfigurationErrors_Http3WhereTheHostHasNoQuic_IsRefusedRatherThanDowngraded()
    {
        // Arrange
        var profile = Profile();
        profile.HttpProtocols = [TransportHttpProtocol.Http1, TransportHttpProtocol.Http2, TransportHttpProtocol.Http3];

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: false, DefaultRedirectPort));

        // Assert
        Assert.Contains("Http3", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_Http3WhereTheHostProvidesQuic_IsAccepted()
    {
        // Arrange
        var profile = Profile();
        profile.HttpProtocols = [TransportHttpProtocol.Http3];

        // Act, Assert
        Assert.Empty(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));
    }

    /// <summary>The binder turns any number into an enum value, and a floor nobody declared would be applied as though it had been chosen.</summary>
    [Fact]
    public void FindConfigurationErrors_ATlsVersionNoMemberDeclares_IsRefused()
    {
        // Arrange
        var profile = Profile();
        profile.MinimumTlsVersion = (TransportMinimumTlsVersion)7;

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.StartsWith($"{SectionPath}:Endpoints:0:MinimumTlsVersion", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_ABundleAndPemMaterialTogether_IsRefusedAsAmbiguous()
    {
        // Arrange
        var profile = Profile();
        profile.ServerCertificate = new TlsServerCertificateOptions
        {
            Bundle = Block("bundle"),
            CertificateChain = Block("chain"),
            PrivateKey = Block("key"),
        };

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.StartsWith($"{SectionPath}:Endpoints:0:ServerCertificate", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_NoCertificateMaterialAtAll_IsRefused()
    {
        // Arrange
        var profile = Profile();
        profile.ServerCertificate = new TlsServerCertificateOptions();

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.StartsWith($"{SectionPath}:Endpoints:0:ServerCertificate", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_ACertificateChainWithNoPrivateKey_IsRefused()
    {
        // Arrange
        var profile = Profile();
        profile.ServerCertificate = new TlsServerCertificateOptions { CertificateChain = Block("chain") };

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.StartsWith($"{SectionPath}:Endpoints:0:ServerCertificate:PrivateKey", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_APrivateKeyWithNoCertificateChain_IsRefused()
    {
        // Arrange
        var profile = Profile();
        profile.ServerCertificate = new TlsServerCertificateOptions { PrivateKey = Block("key") };

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.StartsWith($"{SectionPath}:Endpoints:0:ServerCertificate:CertificateChain", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_TwoProfilesSharingAName_IsRefused()
    {
        // Arrange
        var options = With(Profile(name: "public"), Profile(name: "PUBLIC", domain: "other.example.test"));

        // Act
        var error = Assert.Single(options.FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.Contains("names more than one HTTPS profile", error, StringComparison.Ordinal);
    }

    /// <summary>Which certificate a handshake receives would otherwise be decided by configuration order.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoProfilesPublishingOneDomain_IsRefused()
    {
        // Arrange
        var options = With(Profile(name: "first"), Profile(name: "second"));

        // Act
        var error = Assert.Single(options.FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.Contains("published by more than one HTTPS profile", error, StringComparison.Ordinal);
    }

    /// <summary>ALPN offers what the listener was bound with, which is settled before any server name is known.</summary>
    [Fact]
    public void FindConfigurationErrors_ProfilesSharingAListenerWithDifferentHttpVersions_IsRefused()
    {
        // Arrange
        var first = Profile(name: "first", domain: "one.example.test");
        var second = Profile(name: "second", domain: "two.example.test");
        second.HttpProtocols = [TransportHttpProtocol.Http1];

        // Act
        var error = Assert.Single(With(first, second).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.Contains("name different HTTP versions", error, StringComparison.Ordinal);
    }

    /// <summary>The floor is settled per connection, once the server name is known, so neighbours on one address may differ.</summary>
    [Fact]
    public void FindConfigurationErrors_ProfilesSharingAListenerWithDifferentTlsFloors_IsAccepted()
    {
        // Arrange
        var first = Profile(name: "first", domain: "one.example.test");
        var second = Profile(name: "second", domain: "two.example.test");
        second.MinimumTlsVersion = TransportMinimumTlsVersion.Tls13;

        // Act, Assert
        Assert.Empty(With(first, second).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));
    }

    [Fact]
    public void FindConfigurationErrors_ProfilesOnDifferentPortsWithDifferentHttpVersions_IsAccepted()
    {
        // Arrange
        var first = Profile(name: "first", domain: "one.example.test");
        var second = Profile(name: "second", domain: "two.example.test");
        second.Port = 9443;
        second.HttpProtocols = [TransportHttpProtocol.Http1];

        // Act, Assert
        Assert.Empty(With(first, second).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));
    }

    /// <summary>The wildcard socket already owns the specific address, so the second bind fails with an address-in-use error naming a socket rather than a profile.</summary>
    [Fact]
    public void FindConfigurationErrors_AWildcardAndASpecificAddressOnOnePort_IsRefused()
    {
        // Arrange
        var everyInterface = Profile(name: "public", domain: "one.example.test");
        var oneInterface = Profile(name: "internal", domain: "two.example.test");
        oneInterface.BindAddress = "127.0.0.1";

        // Act
        var error = Assert.Single(With(everyInterface, oneInterface)
            .FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.Contains("already accepts the connections", error, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1", error, StringComparison.Ordinal);
    }

    /// <summary>Kestrel binds the IPv6 wildcard as a dual-mode socket, so it owns the IPv4 addresses of that port as well.</summary>
    [Fact]
    public void FindConfigurationErrors_TheIpv6WildcardBesideAnIpv4AddressOnOnePort_IsRefused()
    {
        // Arrange
        var everyInterface = Profile(name: "public", domain: "one.example.test");
        everyInterface.BindAddress = "::";
        var oneInterface = Profile(name: "internal", domain: "two.example.test");
        oneInterface.BindAddress = "10.0.0.5";

        // Act
        var error = Assert.Single(With(everyInterface, oneInterface)
            .FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));

        // Assert
        Assert.Contains("already accepts the connections", error, StringComparison.Ordinal);
    }

    /// <summary>Profiles naming one address share a listener and are told apart by server name, which is the arrangement the section exists to serve.</summary>
    [Fact]
    public void FindConfigurationErrors_ProfilesNamingTheSameAddressAndPort_IsAccepted()
    {
        // Arrange
        var first = Profile(name: "first", domain: "one.example.test");
        var second = Profile(name: "second", domain: "two.example.test");

        // Act, Assert
        Assert.Empty(With(first, second).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));
    }

    [Fact]
    public void FindConfigurationErrors_AWildcardAndASpecificAddressOnDifferentPorts_IsAccepted()
    {
        // Arrange
        var everyInterface = Profile(name: "public", domain: "one.example.test");
        var oneInterface = Profile(name: "internal", domain: "two.example.test");
        oneInterface.BindAddress = "127.0.0.1";
        oneInterface.Port = 9443;

        // Act, Assert
        Assert.Empty(With(everyInterface, oneInterface).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));
    }

    /// <summary>Two specific addresses are two sockets the operating system grants independently.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoSpecificAddressesOnOnePort_IsAccepted()
    {
        // Arrange
        var first = Profile(name: "first", domain: "one.example.test");
        first.BindAddress = "127.0.0.1";
        var second = Profile(name: "second", domain: "two.example.test");
        second.BindAddress = "10.0.0.5";

        // Act, Assert
        Assert.Empty(With(first, second).FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));
    }

    /// <summary>Enabling TLS should not read as an outage, which is why a deployment gets the redirect without asking.</summary>
    [Fact]
    public void RedirectsClearText_ASurfaceTerminatingTlsThatStatedNothing_RedirectsByDefault()
    {
        // Arrange
        var options = With(Profile());

        // Act, Assert
        Assert.True(options.RedirectsClearText);
        Assert.Empty(options.FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));
    }

    /// <summary>A deployment behind a proxy that already answers the clear-text port turns it off rather than binding a port it did not ask for.</summary>
    [Fact]
    public void RedirectsClearText_ARedirectTurnedOff_BindsNoClearTextListener()
    {
        // Arrange
        var options = With(Profile());
        options.Redirect.Enabled = false;

        // Act, Assert
        Assert.False(options.RedirectsClearText);
        Assert.Empty(options.FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));
    }

    /// <summary>There is no clear-text listener to redirect away from, because the surface is already served over one.</summary>
    [Fact]
    public void RedirectsClearText_ASurfaceTerminatingNoTls_RedirectsNothingDespiteTheDefault() =>
        Assert.False(new TransportHttpsOptions().RedirectsClearText);

    /// <summary>
    /// The default has to stay silent where it means nothing, or every clear-text deployment would fail startup over a
    /// section it never wrote.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_ARedirectLeftAtItsDefaultWithoutAnyProfile_IsNotReported() =>
        Assert.Empty(new TransportHttpsOptions().FindConfigurationErrors(
            SectionPath,
            http3Supported: true,
            DefaultRedirectPort));

    /// <summary>Configured-but-unbound is refused rather than ignored, the same way every other unread setting here is.</summary>
    [Fact]
    public void FindConfigurationErrors_ARedirectStatedWithoutAnyProfile_IsRefused()
    {
        // Arrange
        var options = new TransportHttpsOptions();
        options.Redirect.MarkStated();

        // Act
        var error = Assert.Single(options.FindConfigurationErrors(
            SectionPath,
            http3Supported: true,
            DefaultRedirectPort));

        // Assert
        Assert.StartsWith($"{SectionPath}:Redirect", error, StringComparison.Ordinal);
        Assert.Contains("nothing to redirect to", error, StringComparison.Ordinal);
    }

    /// <summary>One socket cannot serve both schemes, and the operating system would report it as an address already in use.</summary>
    [Fact]
    public void FindConfigurationErrors_ARedirectPortAProfileAlreadyBinds_IsRefused()
    {
        // Arrange
        var profile = Profile();
        var options = With(profile);
        options.Redirect.Port = profile.Port;

        // Act
        var error = Assert.Single(options.FindConfigurationErrors(
            SectionPath,
            http3Supported: true,
            DefaultRedirectPort));

        // Assert
        Assert.StartsWith($"{SectionPath}:Redirect:Port", error, StringComparison.Ordinal);
        Assert.Contains("one socket cannot serve both schemes", error, StringComparison.Ordinal);
    }

    /// <summary>The same collision, reached through the default rather than through a stated port.</summary>
    [Fact]
    public void FindConfigurationErrors_ADefaultRedirectPortAProfileBinds_IsRefused()
    {
        // Arrange
        var profile = Profile();
        profile.Port = DefaultRedirectPort;

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(
            SectionPath,
            http3Supported: true,
            DefaultRedirectPort));

        // Assert
        Assert.StartsWith($"{SectionPath}:Redirect:Port", error, StringComparison.Ordinal);
    }

    /// <summary>A collision with a port nothing binds is no collision, so a redirect that is off is not checked against the profiles.</summary>
    [Fact]
    public void FindConfigurationErrors_ADisabledRedirectSharingAProfilePort_IsNotReported()
    {
        // Arrange
        var profile = Profile();
        var options = With(profile);
        options.Redirect.Enabled = false;
        options.Redirect.Port = profile.Port;

        // Act, Assert
        Assert.Empty(options.FindConfigurationErrors(SectionPath, http3Supported: true, DefaultRedirectPort));
    }

    [Fact]
    public void FindConfigurationErrors_ARedirectBindAddressThatIsNotAnAddress_IsRefused()
    {
        // Arrange
        var options = With(Profile());
        options.Redirect.BindAddress = "not-an-address";

        // Act
        var error = Assert.Single(options.FindConfigurationErrors(
            SectionPath,
            http3Supported: true,
            DefaultRedirectPort));

        // Assert
        Assert.StartsWith($"{SectionPath}:Redirect:BindAddress", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void FindConfigurationErrors_ARedirectPortOutsideTheRange_IsRefused(int port)
    {
        // Arrange
        var options = With(Profile());
        options.Redirect.Port = port;

        // Act
        var error = Assert.Single(options.FindConfigurationErrors(
            SectionPath,
            http3Supported: true,
            DefaultRedirectPort));

        // Assert
        Assert.StartsWith($"{SectionPath}:Redirect:Port", error, StringComparison.Ordinal);
        Assert.Contains("is not a TCP port", error, StringComparison.Ordinal);
    }

    /// <summary>The socket the binder opens, which is the whole of what it decides from this section.</summary>
    [Fact]
    public void ListenerAddress_ARedirectStatingNoPort_BindsEveryIpv4AddressOnTheSurfacesDefault()
    {
        // Arrange
        var options = With(Profile());

        // Act
        var address = options.Redirect.ListenerAddress(DefaultRedirectPort);

        // Assert
        Assert.Equal(IPAddress.Any, address.Address);
        Assert.Equal(DefaultRedirectPort, address.Port);
    }

    [Fact]
    public void ListenerAddress_ARedirectStatingBoth_BindsWhatItStated()
    {
        // Arrange
        var options = With(Profile());
        options.Redirect.BindAddress = "127.0.0.1";
        options.Redirect.Port = 8888;

        // Act
        var address = options.Redirect.ListenerAddress(DefaultRedirectPort);

        // Assert
        Assert.Equal(IPAddress.Loopback, address.Address);
        Assert.Equal(8888, address.Port);
    }

    /// <summary>What a composed redirect resolves a client's host against, one entry per profile.</summary>
    [Fact]
    public void PublishedDomainPorts_SeveralProfiles_MapEachDomainToItsOwnPort()
    {
        // Arrange
        var standard = Profile(name: "public", domain: "one.example.test");
        var managed = Profile(name: "managed", domain: "two.example.test");
        managed.Port = 9443;

        // Act
        var published = With(standard, managed).PublishedDomainPorts();

        // Assert
        Assert.Equal(8443, published["one.example.test"]);
        Assert.Equal(9443, published["two.example.test"]);
    }

    /// <summary>A client sends a host name without regard to case, so the lookup a redirect performs cannot depend on it.</summary>
    [Fact]
    public void PublishedDomainPorts_ADomainInAnotherCase_StillResolves() =>
        Assert.Equal(8443, With(Profile()).PublishedDomainPorts()["MAIL.EXAMPLE.TEST"]);

    private static TransportHttpsOptions With(params TransportHttpsEndpointOptions[] profiles)
    {
        var options = new TransportHttpsOptions();

        foreach (var profile in profiles)
        {
            options.Endpoints.Add(profile);
        }

        return options;
    }

    private static TransportHttpsEndpointOptions Profile(string name = "public", string domain = "mail.example.test") => new()
    {
        Name = name,
        Domain = domain,
        ServerCertificate = new TlsServerCertificateOptions { Bundle = Block("bundle") },
    };

    private static ConfiguredSecret Block(string name) => new()
    {
        Name = name,
        SecretReference = $"file:/etc/mailfathom/tls/{name}",
    };
}
