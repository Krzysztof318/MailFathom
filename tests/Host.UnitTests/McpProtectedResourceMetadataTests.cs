// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Security;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers where the MCP endpoint tells a client to go and authorize.</summary>
public sealed class McpProtectedResourceMetadataTests
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
    public void AddressFor_AConfiguredResource_PublishesUnderThatResourcesAuthority(
        string canonicalResource,
        string expectedAddress) =>
        Assert.Equal(expectedAddress, McpProtectedResourceMetadata.AddressFor(canonicalResource));

    /// <summary>The SDK publishes the document from a request handler rather than a route, so composition needs the path alone to put a middleware in front of it.</summary>
    [Theory]
    [InlineData("https://mail.example.test/mcp", "/.well-known/oauth-protected-resource/mcp")]
    [InlineData("https://mail.example.test:8443/mcp", "/.well-known/oauth-protected-resource/mcp")]
    [InlineData("https://mail.example.test", "/.well-known/oauth-protected-resource")]
    public void PathFor_AConfiguredResource_DropsTheAuthorityTheMiddlewareCannotMatchOn(
        string canonicalResource,
        string expectedPath) =>
        Assert.Equal(expectedPath, McpProtectedResourceMetadata.PathFor(canonicalResource));
}
