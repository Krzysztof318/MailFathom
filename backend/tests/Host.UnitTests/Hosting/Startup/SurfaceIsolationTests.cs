// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting.Startup;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Startup;

/// <summary>Covers which paths a listener answers, given the surfaces composed onto it.</summary>
/// <remarks>
/// The rule runs in both directions and the second matters as much as the first: a listener that does not serve the
/// probes refuses a probe path, and one that serves only the probes refuses everything else. Sharing a port widens what
/// that listener answers and nothing else — a path is still served only by the surface that owns it.
/// </remarks>
public sealed class SurfaceIsolationTests
{
    /// <summary>What every test below states unless it is about the documentation surface itself.</summary>
    /// <remarks>A development process is the permissive setting, so a refusal asserted under it is refused under every process this host runs as.</remarks>
    private const bool DocumentationIsPublished = true;

    [Fact]
    public void ListenerServesPath_AProbePathWhereTheProbesAreServed_IsServed() =>
        Assert.True(SurfaceIsolation.ListenerServesPath(ServedSurfaces.Probes, "/health", DocumentationIsPublished));

    /// <summary>An unauthenticated dependency report must not answer on a port published for something else.</summary>
    [Fact]
    public void ListenerServesPath_AProbePathWhereTheProbesAreNotServed_IsRefused() =>
        Assert.False(SurfaceIsolation.ListenerServesPath(
            ServedSurfaces.Mcp | ServedSurfaces.Admin,
            "/health",
            DocumentationIsPublished));

    [Fact]
    public void ListenerServesPath_AnAdministrativePathWhereTheSurfaceIsServed_IsServed() =>
        Assert.True(SurfaceIsolation.ListenerServesPath(ServedSurfaces.Admin, "/api/admin/accounts", DocumentationIsPublished));

    [Fact]
    public void ListenerServesPath_AnAdministrativePathWhereTheSurfaceIsNotServed_IsRefused() =>
        Assert.False(SurfaceIsolation.ListenerServesPath(
            ServedSurfaces.Mcp | ServedSurfaces.Probes,
            "/api/admin/accounts",
            DocumentationIsPublished));

    [Fact]
    public void ListenerServesPath_AClientPathWhereTheSurfaceIsServed_IsServed() =>
        Assert.True(SurfaceIsolation.ListenerServesPath(ServedSurfaces.Client, "/api/client/session", DocumentationIsPublished));

    /// <summary>A mailbox served to a client must not answer on a port published for an agent or an operator.</summary>
    [Fact]
    public void ListenerServesPath_AClientPathWhereTheSurfaceIsNotServed_IsRefused() =>
        Assert.False(SurfaceIsolation.ListenerServesPath(
            ServedSurfaces.Mcp | ServedSurfaces.Admin | ServedSurfaces.Probes,
            "/api/client/session",
            DocumentationIsPublished));

    /// <summary>The MCP surface owns whatever the other three do not claim, so a path added to it later needs no rule of its own.</summary>
    [Fact]
    public void ListenerServesPath_TheMcpRouteWhereTheEndpointIsServed_IsServed() =>
        Assert.True(SurfaceIsolation.ListenerServesPath(ServedSurfaces.Mcp, "/mcp", DocumentationIsPublished));

    [Fact]
    public void ListenerServesPath_TheMcpRouteWhereTheEndpointIsNotServed_IsRefused() =>
        Assert.False(SurfaceIsolation.ListenerServesPath(
            ServedSurfaces.Admin | ServedSurfaces.Probes,
            "/mcp",
            DocumentationIsPublished));

    /// <summary>A shared port answers every path its surfaces own, which is the whole point of sharing one.</summary>
    [Fact]
    public void ListenerServesPath_APortServingEverySurface_AnswersEveryPath()
    {
        // Arrange
        const ServedSurfaces everySurface =
            ServedSurfaces.Mcp | ServedSurfaces.Admin | ServedSurfaces.Client | ServedSurfaces.Probes;

        // Act, Assert
        Assert.True(SurfaceIsolation.ListenerServesPath(everySurface, "/mcp", DocumentationIsPublished));
        Assert.True(SurfaceIsolation.ListenerServesPath(everySurface, "/api/admin/accounts", DocumentationIsPublished));
        Assert.True(SurfaceIsolation.ListenerServesPath(everySurface, "/api/client/session", DocumentationIsPublished));
        Assert.True(SurfaceIsolation.ListenerServesPath(everySurface, "/health", DocumentationIsPublished));
    }

    /// <summary>A port this process did not bind serves nothing rather than falling back to serving everything.</summary>
    [Fact]
    public void ListenerServesPath_APortNoSurfaceIsComposedOnto_RefusesEveryPath()
    {
        Assert.False(SurfaceIsolation.ListenerServesPath(ServedSurfaces.None, "/mcp", DocumentationIsPublished));
        Assert.False(SurfaceIsolation.ListenerServesPath(
            ServedSurfaces.None,
            "/api/admin/accounts",
            DocumentationIsPublished));
        Assert.False(SurfaceIsolation.ListenerServesPath(
            ServedSurfaces.None,
            "/api/client/session",
            DocumentationIsPublished));
        Assert.False(SurfaceIsolation.ListenerServesPath(ServedSurfaces.None, "/health", DocumentationIsPublished));
    }

    /// <summary>RFC 9728 puts the document at the root, and its only reader arrives on the administrative listener without a credential yet.</summary>
    [Fact]
    public void ListenerServesPath_TheAdministrativeMetadataDocument_FollowsTheAdministrativeSurface()
    {
        // Arrange
        const string metadataPath = "/.well-known/oauth-protected-resource/api/admin";

        // Act, Assert
        Assert.True(SurfaceIsolation.ListenerServesPath(ServedSurfaces.Admin, metadataPath, DocumentationIsPublished));
        Assert.False(SurfaceIsolation.ListenerServesPath(ServedSurfaces.Mcp, metadataPath, DocumentationIsPublished));
    }

    /// <summary>The client's document sits at the root for the same reason, and its reader arrives on the client listener holding nothing.</summary>
    [Fact]
    public void ListenerServesPath_TheClientMetadataDocument_FollowsTheClientSurface()
    {
        // Arrange
        const string metadataPath = "/.well-known/oauth-protected-resource/api/client";

        // Act, Assert
        Assert.True(SurfaceIsolation.ListenerServesPath(ServedSurfaces.Client, metadataPath, DocumentationIsPublished));
        Assert.False(SurfaceIsolation.ListenerServesPath(
            ServedSurfaces.Mcp | ServedSurfaces.Admin,
            metadataPath,
            DocumentationIsPublished));
    }

    /// <summary>
    /// The documentation surface belongs to no surface, because it describes two of them and exists only in a
    /// development process. The listener a developer has open is whichever one they enabled, so every bound one
    /// answers it — including the administrative listener, where the MCP catch-all would have refused it.
    /// </summary>
    [Fact]
    public void ListenerServesPath_ADocumentationPathOnABoundListener_IsServed()
    {
        // Arrange
        ServedSurfaces[] boundListeners =
            [ServedSurfaces.Admin, ServedSurfaces.Client, ServedSurfaces.Mcp, ServedSurfaces.Probes];

        // Act, Assert
        Assert.All(
            boundListeners,
            served => Assert.True(SurfaceIsolation.ListenerServesPath(served, "/openapi/v1.json", DocumentationIsPublished)));
        Assert.All(
            boundListeners,
            served => Assert.True(SurfaceIsolation.ListenerServesPath(served, "/scalar", DocumentationIsPublished)));
    }

    /// <summary>
    /// The exemption lasts exactly as long as the routes do. A process that maps no documentation refuses these paths
    /// here, where every other unserved path is refused, rather than letting them past CORS, authentication, the
    /// client-certificate check, and the rate limiter to be answered <c>404</c> by routing — on a listener carrying
    /// nothing but the credential-free probes among others.
    /// </summary>
    [Fact]
    public void ListenerServesPath_ADocumentationPathWhereTheDocumentationIsNotPublished_IsRefused()
    {
        // Arrange
        ServedSurfaces[] boundListeners =
            [ServedSurfaces.Admin, ServedSurfaces.Client, ServedSurfaces.Mcp, ServedSurfaces.Probes];

        // Act, Assert
        Assert.All(
            boundListeners,
            served => Assert.False(SurfaceIsolation.ListenerServesPath(served, "/openapi/v1.json", documentationIsPublished: false)));
        Assert.All(
            boundListeners,
            served => Assert.False(SurfaceIsolation.ListenerServesPath(served, "/scalar", documentationIsPublished: false)));
    }

    /// <summary>A port this process did not bind still serves nothing, documentation included.</summary>
    [Fact]
    public void ListenerServesPath_ADocumentationPathOnAPortNoSurfaceIsComposedOnto_IsRefused()
    {
        Assert.False(SurfaceIsolation.ListenerServesPath(ServedSurfaces.None, "/openapi/v1.json", DocumentationIsPublished));
        Assert.False(SurfaceIsolation.ListenerServesPath(ServedSurfaces.None, "/scalar", DocumentationIsPublished));
    }

    /// <summary>Matched by segment, so a path that merely starts with the same letters is not mistaken for one of these.</summary>
    [Fact]
    public void IsAdminPath_APathSharingThePrefixesLetters_IsNotAdministrative() =>
        Assert.False(SurfaceIsolation.IsAdminPath("/api/administrators"));

    /// <summary>The same segment rule on the client prefix, which shares its letters with a plausible route of its own.</summary>
    [Fact]
    public void IsClientPath_APathSharingThePrefixesLetters_IsNotTheClientSurface() =>
        Assert.False(SurfaceIsolation.IsClientPath("/api/clients"));
}
