// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Transport;

/// <summary>Covers where a protected surface tells a client to go and authorize.</summary>
public sealed class ProtectedResourceMetadataAddressTests
{
    /// <summary>
    /// The address is composed from the configured resource rather than from the request asking for it. Derived from the
    /// request, a deployment behind a reverse proxy would tell each client to authenticate for whichever name that client
    /// arrived under, including one an attacker chose.
    /// </summary>
    [Theory]
    [InlineData("https://mail.example.test/mcp", "https://mail.example.test/.well-known/oauth-protected-resource/mcp")]
    [InlineData("https://mail.example.test", "https://mail.example.test/.well-known/oauth-protected-resource")]
    [InlineData("https://mail.example.test/", "https://mail.example.test/.well-known/oauth-protected-resource")]
    [InlineData("https://mail.example.test:8443/mcp", "https://mail.example.test:8443/.well-known/oauth-protected-resource/mcp")]
    [InlineData("https://mail.example.test:8090/api/admin", "https://mail.example.test:8090/.well-known/oauth-protected-resource/api/admin")]
    public void AddressFor_AConfiguredResource_PublishesUnderThatResourcesAuthority(
        string canonicalResource,
        string expectedAddress) =>
        Assert.Equal(expectedAddress, ProtectedResourceMetadataAddress.AddressFor(canonicalResource));

    /// <summary>The MCP SDK publishes the document from a request handler rather than a route, so composition needs the path alone to put a middleware in front of it.</summary>
    [Theory]
    [InlineData("https://mail.example.test/mcp", "/.well-known/oauth-protected-resource/mcp")]
    [InlineData("https://mail.example.test:8443/mcp", "/.well-known/oauth-protected-resource/mcp")]
    [InlineData("https://mail.example.test", "/.well-known/oauth-protected-resource")]
    public void PathFor_AConfiguredResource_DropsTheAuthorityTheMiddlewareCannotMatchOn(
        string canonicalResource,
        string expectedPath) =>
        Assert.Equal(expectedPath, ProtectedResourceMetadataAddress.PathFor(canonicalResource));

    /// <summary>
    /// The claim that lets a client find the document before it has read anything: composing from the route prefix it is
    /// about to call reaches the same path the resource identifier produces. Were these to disagree, <c>mfctl</c> would
    /// ask for the document where the deployment does not publish it, and OAuth sign-in would be unreachable.
    /// </summary>
    [Fact]
    public void BeneathRoutePrefix_TheAdministrativePrefix_AgreesWithTheResourceItIsRequiredToName() =>
        Assert.Equal(
            ProtectedResourceMetadataAddress.PathFor($"https://mail.example.test:8090{AdminEndpointOptions.RoutePrefix}"),
            ProtectedResourceMetadataAddress.BeneathRoutePrefix(AdminEndpointOptions.RoutePrefix));

    /// <summary>A prefix written with a trailing slash must not produce a doubled separator that matches no request.</summary>
    [Theory]
    [InlineData("/api/admin", "/.well-known/oauth-protected-resource/api/admin")]
    [InlineData("/api/admin/", "/.well-known/oauth-protected-resource/api/admin")]
    public void BeneathRoutePrefix_APrefix_AppendsItToTheWellKnownSegment(string routePrefix, string expectedPath) =>
        Assert.Equal(expectedPath, ProtectedResourceMetadataAddress.BeneathRoutePrefix(routePrefix));
}
