// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Endpoints;

/// <summary>Covers which sockets the process opens when surfaces share a port, and which sharings are refused.</summary>
/// <remarks>
/// Sharing is the posture a single-node deployment wants and the reason both request-serving surfaces default to the
/// same ports. What every rule here protects is the other half of it: two surfaces on one socket must agree about that
/// socket, because a second bind fails with an address-in-use error naming a socket rather than a section, and binding
/// once from whichever section came first would serve its posture to the other's clients.
/// </remarks>
public sealed class ListenerCompositionTests
{
    [Fact]
    public void Compose_TwoSurfacesAgreeingOnOnePort_OpensOneSocketServingBoth()
    {
        // Arrange
        DeclaredListener[] declarations = [ClearText("McpEndpoint", ServedSurfaces.Mcp), ClearText("AdminEndpoint", ServedSurfaces.Admin)];

        // Act
        var composed = ListenerComposition.Compose(declarations);

        // Assert
        Assert.Empty(composed.Errors);
        var listener = Assert.Single(composed.Listeners);
        Assert.Equal(ServedSurfaces.Mcp | ServedSurfaces.Admin, listener.Surfaces);
        Assert.Equal(8080, listener.Address.Port);
    }

    /// <summary>All three on one port is the posture that publishes one socket, and it is accepted rather than merely tolerated.</summary>
    [Fact]
    public void Compose_EverySurfaceOnOnePort_OpensOneSocketServingAllOfThem()
    {
        // Arrange
        DeclaredListener[] declarations =
        [
            ClearText("McpEndpoint", ServedSurfaces.Mcp),
            ClearText("AdminEndpoint", ServedSurfaces.Admin),
            ClearText("HealthEndpoints", ServedSurfaces.Probes),
        ];

        // Act
        var composed = ListenerComposition.Compose(declarations);

        // Assert
        Assert.Empty(composed.Errors);
        Assert.Equal(
            ServedSurfaces.Mcp | ServedSurfaces.Admin | ServedSurfaces.Probes,
            Assert.Single(composed.Listeners).Surfaces);
    }

    /// <summary>The failure the owner named: one socket cannot both redirect to TLS and serve the routes in clear text.</summary>
    [Fact]
    public void Compose_OneSurfaceRedirectingWhereAnotherServes_IsRefused()
    {
        // Arrange
        DeclaredListener[] declarations =
        [
            ClearText("McpEndpoint", ServedSurfaces.Mcp) with { RedirectsClearText = false },
            ClearText("AdminEndpoint", ServedSurfaces.Admin) with { RedirectsClearText = true },
        ];

        // Act
        var error = Assert.Single(ListenerComposition.Compose(declarations).Errors);

        // Assert
        Assert.Contains("One socket cannot do both", error, StringComparison.Ordinal);
        Assert.Contains("McpEndpoint", error, StringComparison.Ordinal);
        Assert.Contains("AdminEndpoint", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_OneSurfaceTerminatingTlsWhereAnotherServesClearText_IsRefused()
    {
        // Arrange
        DeclaredListener[] declarations = [ClearText("McpEndpoint", ServedSurfaces.Mcp), Profiles("AdminEndpoint", ServedSurfaces.Admin, port: 8080)];

        // Act
        var error = Assert.Single(ListenerComposition.Compose(declarations).Errors);

        // Assert
        Assert.Contains("whether it carries TLS", error, StringComparison.Ordinal);
    }

    /// <summary>The probes present one certificate and the endpoints select one by server name; a socket answers a handshake one way.</summary>
    [Fact]
    public void Compose_AProbeCertificateSharingASocketWithProfiles_IsRefused()
    {
        // Arrange
        DeclaredListener[] declarations =
        [
            Profiles("McpEndpoint", ServedSurfaces.Mcp, port: 8443),
            ProbeTls("HealthEndpoints", port: 8443),
        ];

        // Act
        var error = Assert.Single(ListenerComposition.Compose(declarations).Errors);

        // Assert
        Assert.Contains("presents one certificate to every connection", error, StringComparison.Ordinal);
    }

    /// <summary>Whether a certificate is asked for is settled while the connection is established, so it is one answer for the socket.</summary>
    [Fact]
    public void Compose_OnlyOneSurfaceAskingForAClientCertificate_IsRefused()
    {
        // Arrange
        DeclaredListener[] declarations =
        [
            Profiles("McpEndpoint", ServedSurfaces.Mcp, port: 8443, domain: "mail.example.test") with { RequestsClientCertificates = true },
            Profiles("AdminEndpoint", ServedSurfaces.Admin, port: 8443, domain: "admin.example.test"),
        ];

        // Act
        var error = Assert.Single(ListenerComposition.Compose(declarations).Errors);

        // Assert
        Assert.Contains("only one asks the client for a certificate", error, StringComparison.Ordinal);
    }

    /// <summary>Two names on one TLS socket is the point of sharing it; the profiles merge and the handshake tells them apart.</summary>
    [Fact]
    public void Compose_TwoSurfacesPublishingDifferentDomainsOnOneTlsSocket_MergesTheirProfiles()
    {
        // Arrange
        DeclaredListener[] declarations =
        [
            Profiles("McpEndpoint", ServedSurfaces.Mcp, port: 8443, domain: "mail.example.test"),
            Profiles("AdminEndpoint", ServedSurfaces.Admin, port: 8443, domain: "admin.example.test"),
        ];

        // Act
        var composed = ListenerComposition.Compose(declarations);

        // Assert
        Assert.Empty(composed.Errors);
        var listener = Assert.Single(composed.Listeners);
        Assert.Equal(
            ["mail.example.test", "admin.example.test"],
            listener.Profiles.Select(static profile => profile.Domain));
    }

    /// <summary>One name served by two surfaces would leave composition order deciding which of them a client reached.</summary>
    [Fact]
    public void Compose_TwoSurfacesPublishingOneDomainOnOneTlsSocket_IsRefused()
    {
        // Arrange
        DeclaredListener[] declarations =
        [
            Profiles("McpEndpoint", ServedSurfaces.Mcp, port: 8443),
            Profiles("AdminEndpoint", ServedSurfaces.Admin, port: 8443),
        ];

        // Act
        var error = Assert.Single(ListenerComposition.Compose(declarations).Errors);

        // Assert
        Assert.Contains("is published on", error, StringComparison.Ordinal);
    }

    /// <summary>Two specific addresses on one port are two sockets the operating system grants independently.</summary>
    [Fact]
    public void Compose_TwoSurfacesOnOnePortAtDifferentAddresses_OpensASocketEach()
    {
        // Arrange
        DeclaredListener[] declarations =
        [
            ClearText("McpEndpoint", ServedSurfaces.Mcp) with { BindAddress = "10.0.0.1" },
            ClearText("AdminEndpoint", ServedSurfaces.Admin) with { BindAddress = "10.0.0.2" },
        ];

        // Act
        var composed = ListenerComposition.Compose(declarations);

        // Assert
        Assert.Empty(composed.Errors);
        Assert.Equal(2, composed.Listeners.Count);
    }

    /// <summary>The wildcard already accepts what the specific address would receive, so only one of the two could bind.</summary>
    [Fact]
    public void Compose_AWildcardBesideASpecificAddressOnOnePort_IsRefused()
    {
        // Arrange
        DeclaredListener[] declarations =
        [
            ClearText("McpEndpoint", ServedSurfaces.Mcp),
            ClearText("AdminEndpoint", ServedSurfaces.Admin) with { BindAddress = "10.0.0.2" },
        ];

        // Act
        var error = Assert.Single(ListenerComposition.Compose(declarations).Errors);

        // Assert
        Assert.Contains("already accepts the connections", error, StringComparison.Ordinal);
    }

    /// <summary>A surface alone on its port agrees with itself, so nothing about sharing is reported against it.</summary>
    [Fact]
    public void Compose_SurfacesOnPortsOfTheirOwn_ReportsNothing()
    {
        // Arrange
        DeclaredListener[] declarations =
        [
            ClearText("McpEndpoint", ServedSurfaces.Mcp),
            ClearText("HealthEndpoints", ServedSurfaces.Probes) with { Port = 8081 },
        ];

        // Act
        var composed = ListenerComposition.Compose(declarations);

        // Assert
        Assert.Empty(composed.Errors);
        Assert.Equal(2, composed.Listeners.Count);
    }

    [Fact]
    public void SurfacesByPort_APortTwoSurfacesShare_ReportsTheUnion()
    {
        // Arrange
        DeclaredListener[] declarations =
        [
            ClearText("McpEndpoint", ServedSurfaces.Mcp),
            ClearText("AdminEndpoint", ServedSurfaces.Admin),
            ClearText("HealthEndpoints", ServedSurfaces.Probes) with { Port = 8081 },
        ];

        // Act
        var byPort = ListenerComposition.Compose(declarations).SurfacesByPort();

        // Assert
        Assert.Equal(ServedSurfaces.Mcp | ServedSurfaces.Admin, byPort[8080]);
        Assert.Equal(ServedSurfaces.Probes, byPort[8081]);
    }

    [Fact]
    public void Compose_NoSurfaceAskingForASocket_OpensNothing()
    {
        // Act
        var composed = ListenerComposition.Compose([]);

        // Assert
        Assert.Empty(composed.Listeners);
        Assert.Empty(composed.Errors);
    }

    /// <summary>Two surfaces redirecting one shared clear-text socket to HTTPS ports of their own, which is the case the merge exists for.</summary>
    [Fact]
    public void Compose_TwoSurfacesRedirectingOneSocketToTheirOwnHttpsPorts_MergesTheirTargets()
    {
        // Arrange
        DeclaredListener[] declarations =
        [
            Redirecting("McpEndpoint", ServedSurfaces.Mcp, "mail.example.test", 8443),
            Redirecting("AdminEndpoint", ServedSurfaces.Admin, "admin.example.test", 9443),
        ];

        // Act
        var composed = ListenerComposition.Compose(declarations);

        // Assert
        Assert.Empty(composed.Errors);
        var listener = Assert.Single(composed.Listeners);
        Assert.Equal(8443, listener.RedirectTargets["mail.example.test"]);
        Assert.Equal(9443, listener.RedirectTargets["admin.example.test"]);
    }

    /// <summary>One name at two addresses leaves the client's own host header with two answers, decided by composition order.</summary>
    [Fact]
    public void Compose_TwoSurfacesRedirectingOneNameToDifferentPorts_IsRefused()
    {
        // Arrange
        DeclaredListener[] declarations =
        [
            Redirecting("McpEndpoint", ServedSurfaces.Mcp, "mail.example.test", 8443),
            Redirecting("AdminEndpoint", ServedSurfaces.Admin, "mail.example.test", 9443),
        ];

        // Act
        var error = Assert.Single(ListenerComposition.Compose(declarations).Errors);

        // Assert
        Assert.Contains("on different HTTPS ports", error, StringComparison.Ordinal);
    }

    /// <summary>The same name at the same address says nothing two surfaces disagree about.</summary>
    [Fact]
    public void Compose_TwoSurfacesRedirectingOneNameToOnePort_IsAccepted()
    {
        // Arrange
        DeclaredListener[] declarations =
        [
            Redirecting("McpEndpoint", ServedSurfaces.Mcp, "mail.example.test", 8443),
            Redirecting("AdminEndpoint", ServedSurfaces.Admin, "mail.example.test", 8443),
        ];

        // Act, Assert
        Assert.Empty(ListenerComposition.Compose(declarations).Errors);
    }

    private static DeclaredListener Redirecting(
        string sectionName,
        ServedSurfaces surface,
        string domain,
        int httpsPort) =>
        ClearText(sectionName, surface) with
        {
            RedirectsClearText = true,
            RedirectTargets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [domain] = httpsPort },
        };

    private static DeclaredListener ClearText(string sectionName, ServedSurfaces surface) => new(
        sectionName,
        surface,
        BindAddress: "0.0.0.0",
        Port: 8080,
        TerminatesTls: false,
        RedirectsClearText: false,
        PresentsProfiles: false,
        Profiles: [],
        RequestsClientCertificates: false,
        RedirectTargets: new Dictionary<string, int>());

    private static DeclaredListener Profiles(
        string sectionName,
        ServedSurfaces surface,
        int port,
        string domain = "mail.example.test") => new(
        sectionName,
        surface,
        BindAddress: "0.0.0.0",
        port,
        TerminatesTls: true,
        RedirectsClearText: false,
        PresentsProfiles: true,
        [new TransportHttpsEndpointOptions { Name = sectionName, Domain = domain, Port = port }],
        RequestsClientCertificates: false,
        RedirectTargets: new Dictionary<string, int>());

    private static DeclaredListener ProbeTls(string sectionName, int port) => new(
        sectionName,
        ServedSurfaces.Probes,
        BindAddress: "0.0.0.0",
        port,
        TerminatesTls: true,
        RedirectsClearText: false,
        PresentsProfiles: false,
        Profiles: [],
        RequestsClientCertificates: false,
        RedirectTargets: new Dictionary<string, int>());
}
