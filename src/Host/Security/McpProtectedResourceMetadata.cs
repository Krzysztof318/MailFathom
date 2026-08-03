// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Security;

/// <summary>Where the MCP endpoint publishes the RFC 9728 document that tells a client how to authorize.</summary>
/// <remarks>
/// <para>
/// This belongs to the MCP surface alone rather than to OAuth validation generally. Publishing a discovery document is
/// what a protocol surface does for clients that arrive holding nothing and have to find an authorization server; a
/// surface whose clients are configured with a credential before they connect publishes none, and would have no reader
/// for one.
/// </para>
/// <para>
/// Both addresses are composed from the configured resource rather than from the request that asks for them. The MCP SDK
/// will otherwise derive them from the incoming request's scheme and <c>Host</c> header, which behind a reverse proxy
/// means a client is told to authorize for whichever name it happened to arrive under — including one an attacker chose.
/// Composing them here is what keeps the resource identifier a deployment decision.
/// </para>
/// </remarks>
internal static class McpProtectedResourceMetadata
{
    /// <summary>Reports where the protected resource metadata document is published.</summary>
    /// <param name="canonicalResource">The canonical resource identifier the endpoint publishes.</param>
    /// <returns>The absolute address of the RFC 9728 document.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="canonicalResource" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// RFC 9728 places the document under the resource's own authority, with the resource's path appended to the
    /// well-known segment, so <c>https://mail.example.test/mcp</c> publishes at
    /// <c>https://mail.example.test/.well-known/oauth-protected-resource/mcp</c>.
    /// </remarks>
    internal static string AddressFor(string canonicalResource)
    {
        ArgumentNullException.ThrowIfNull(canonicalResource);

        var resource = new Uri(canonicalResource);

        return $"{resource.GetLeftPart(UriPartial.Authority)}/.well-known/oauth-protected-resource{resource.AbsolutePath.TrimEnd('/')}";
    }

    /// <summary>Reports the path of the protected resource metadata document, without its authority.</summary>
    /// <param name="canonicalResource">The canonical resource identifier the endpoint publishes.</param>
    /// <returns>The absolute path the document answers at.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="canonicalResource" /> is <see langword="null" />.</exception>
    /// <remarks>The SDK publishes the document from an authentication request handler rather than from a mapped route, so composition needs the path on its own to put a middleware in front of it.</remarks>
    internal static string PathFor(string canonicalResource) =>
        new Uri(AddressFor(canonicalResource)).AbsolutePath;
}
