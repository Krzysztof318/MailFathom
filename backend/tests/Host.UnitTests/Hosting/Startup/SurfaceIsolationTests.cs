// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
    [Fact]
    public void ListenerServesPath_AProbePathWhereTheProbesAreServed_IsServed() =>
        Assert.True(SurfaceIsolation.ListenerServesPath(ServedSurfaces.Probes, "/health"));

    /// <summary>An unauthenticated dependency report must not answer on a port published for something else.</summary>
    [Fact]
    public void ListenerServesPath_AProbePathWhereTheProbesAreNotServed_IsRefused() =>
        Assert.False(SurfaceIsolation.ListenerServesPath(ServedSurfaces.Mcp | ServedSurfaces.Admin, "/health"));

    [Fact]
    public void ListenerServesPath_AnAdministrativePathWhereTheSurfaceIsServed_IsServed() =>
        Assert.True(SurfaceIsolation.ListenerServesPath(ServedSurfaces.Admin, "/api/admin/accounts"));

    [Fact]
    public void ListenerServesPath_AnAdministrativePathWhereTheSurfaceIsNotServed_IsRefused() =>
        Assert.False(SurfaceIsolation.ListenerServesPath(ServedSurfaces.Mcp | ServedSurfaces.Probes, "/api/admin/accounts"));

    /// <summary>The MCP surface owns whatever the other two do not claim, so a path added to it later needs no rule of its own.</summary>
    [Fact]
    public void ListenerServesPath_TheMcpRouteWhereTheEndpointIsServed_IsServed() =>
        Assert.True(SurfaceIsolation.ListenerServesPath(ServedSurfaces.Mcp, "/mcp"));

    [Fact]
    public void ListenerServesPath_TheMcpRouteWhereTheEndpointIsNotServed_IsRefused() =>
        Assert.False(SurfaceIsolation.ListenerServesPath(ServedSurfaces.Admin | ServedSurfaces.Probes, "/mcp"));

    /// <summary>A shared port answers every path its surfaces own, which is the whole point of sharing one.</summary>
    [Fact]
    public void ListenerServesPath_APortServingEverySurface_AnswersEveryPath()
    {
        // Arrange
        const ServedSurfaces everySurface = ServedSurfaces.Mcp | ServedSurfaces.Admin | ServedSurfaces.Probes;

        // Act, Assert
        Assert.True(SurfaceIsolation.ListenerServesPath(everySurface, "/mcp"));
        Assert.True(SurfaceIsolation.ListenerServesPath(everySurface, "/api/admin/accounts"));
        Assert.True(SurfaceIsolation.ListenerServesPath(everySurface, "/health"));
    }

    /// <summary>A port this process did not bind serves nothing rather than falling back to serving everything.</summary>
    [Fact]
    public void ListenerServesPath_APortNoSurfaceIsComposedOnto_RefusesEveryPath()
    {
        Assert.False(SurfaceIsolation.ListenerServesPath(ServedSurfaces.None, "/mcp"));
        Assert.False(SurfaceIsolation.ListenerServesPath(ServedSurfaces.None, "/api/admin/accounts"));
        Assert.False(SurfaceIsolation.ListenerServesPath(ServedSurfaces.None, "/health"));
    }

    /// <summary>RFC 9728 puts the document at the root, and its only reader arrives on the administrative listener without a credential yet.</summary>
    [Fact]
    public void ListenerServesPath_TheAdministrativeMetadataDocument_FollowsTheAdministrativeSurface()
    {
        // Arrange
        const string metadataPath = "/.well-known/oauth-protected-resource/api/admin";

        // Act, Assert
        Assert.True(SurfaceIsolation.ListenerServesPath(ServedSurfaces.Admin, metadataPath));
        Assert.False(SurfaceIsolation.ListenerServesPath(ServedSurfaces.Mcp, metadataPath));
    }

    /// <summary>Matched by segment, so a path that merely starts with the same letters is not mistaken for one of these.</summary>
    [Fact]
    public void IsAdminPath_APathSharingThePrefixesLetters_IsNotAdministrative() =>
        Assert.False(SurfaceIsolation.IsAdminPath("/api/administrators"));
}
