// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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

    [Fact]
    public void TerminatesTls_NoProfiles_IsTheClearTextPosture()
    {
        // Arrange, Act
        var options = new TransportHttpsOptions();

        // Assert
        Assert.False(options.TerminatesTls);
        Assert.Empty(options.FindConfigurationErrors(SectionPath, http3Supported: true));
    }

    [Fact]
    public void FindConfigurationErrors_AWellFormedProfile_ReportsNothing()
    {
        // Arrange
        var options = With(Profile());

        // Act, Assert
        Assert.True(options.TerminatesTls);
        Assert.Empty(options.FindConfigurationErrors(SectionPath, http3Supported: true));
    }

    [Fact]
    public void FindConfigurationErrors_AProfileWithoutAName_IsRefused()
    {
        // Arrange
        var profile = Profile();
        profile.Name = "   ";

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true));

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
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true));

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
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true));

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
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true));

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
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true));

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
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true));

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
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true));

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
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true));

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
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true));

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
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: false));

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
        Assert.Empty(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true));
    }

    /// <summary>The binder turns any number into an enum value, and a floor nobody declared would be applied as though it had been chosen.</summary>
    [Fact]
    public void FindConfigurationErrors_ATlsVersionNoMemberDeclares_IsRefused()
    {
        // Arrange
        var profile = Profile();
        profile.MinimumTlsVersion = (TransportMinimumTlsVersion)7;

        // Act
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true));

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
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true));

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
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true));

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
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true));

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
        var error = Assert.Single(With(profile).FindConfigurationErrors(SectionPath, http3Supported: true));

        // Assert
        Assert.StartsWith($"{SectionPath}:Endpoints:0:ServerCertificate:CertificateChain", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_TwoProfilesSharingAName_IsRefused()
    {
        // Arrange
        var options = With(Profile(name: "public"), Profile(name: "PUBLIC", domain: "other.example.test"));

        // Act
        var error = Assert.Single(options.FindConfigurationErrors(SectionPath, http3Supported: true));

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
        var error = Assert.Single(options.FindConfigurationErrors(SectionPath, http3Supported: true));

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
        var error = Assert.Single(With(first, second).FindConfigurationErrors(SectionPath, http3Supported: true));

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
        Assert.Empty(With(first, second).FindConfigurationErrors(SectionPath, http3Supported: true));
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
        Assert.Empty(With(first, second).FindConfigurationErrors(SectionPath, http3Supported: true));
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
            .FindConfigurationErrors(SectionPath, http3Supported: true));

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
            .FindConfigurationErrors(SectionPath, http3Supported: true));

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
        Assert.Empty(With(first, second).FindConfigurationErrors(SectionPath, http3Supported: true));
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
        Assert.Empty(With(everyInterface, oneInterface).FindConfigurationErrors(SectionPath, http3Supported: true));
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
        Assert.Empty(With(first, second).FindConfigurationErrors(SectionPath, http3Supported: true));
    }

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
