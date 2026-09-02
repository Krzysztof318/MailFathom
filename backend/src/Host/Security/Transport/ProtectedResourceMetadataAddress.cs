// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Security.Transport;

/// <summary>Where a protected surface publishes the RFC 9728 document that tells a client how to authorize.</summary>
/// <remarks>
/// <para>
/// Both protected surfaces publish one, because both have clients that arrive holding nothing: an MCP client that was
/// handed an address and a <c>mfctl login</c> that is about to sign in. What the document carries is the same in either
/// case — the resource identifier a token must be issued for, the authorization servers this deployment trusts, and the
/// scopes it requires — so where it lives is one rule rather than one per surface.
/// </para>
/// <para>
/// Both addresses are composed from the configured resource rather than from the request that asks for them. Deriving
/// them from the incoming request's scheme and <c>Host</c> header, which is what the MCP SDK does when left to itself,
/// means a client behind a reverse proxy is told to authorize for whichever name it happened to arrive under —
/// including one an attacker chose. Composing them here is what keeps the resource identifier a deployment decision.
/// </para>
/// </remarks>
internal static class ProtectedResourceMetadataAddress
{
    private const string WellKnownSegment = "/.well-known/oauth-protected-resource";

    /// <summary>Reports where the protected resource metadata document is published.</summary>
    /// <param name="canonicalResource">The canonical resource identifier the surface publishes.</param>
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

        return $"{resource.GetLeftPart(UriPartial.Authority)}{WellKnownSegment}{resource.AbsolutePath.TrimEnd('/')}";
    }

    /// <summary>Reports the path of the protected resource metadata document, without its authority.</summary>
    /// <param name="canonicalResource">The canonical resource identifier the surface publishes.</param>
    /// <returns>The absolute path the document answers at.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="canonicalResource" /> is <see langword="null" />.</exception>
    /// <remarks>The MCP SDK publishes its document from an authentication request handler rather than from a mapped route, so composition needs the path on its own to put a middleware in front of it; the administrative surface maps a route at it.</remarks>
    internal static string PathFor(string canonicalResource) =>
        new Uri(AddressFor(canonicalResource)).AbsolutePath;

    /// <summary>Reports the path a surface serving one route prefix publishes its document at.</summary>
    /// <param name="routePrefix">The prefix the surface's routes answer beneath, for example <c>/api/admin</c>.</param>
    /// <returns>The absolute path, composed without needing the resource's authority.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="routePrefix" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The same path <see cref="PathFor" /> produces, reached from the one thing a client knows before it has read
    /// anything: which routes it is about to call. That is what lets <c>mfctl</c> find the document without a prior
    /// round trip, and it is why a surface publishing one requires its resource identifier to name its route prefix.
    /// </remarks>
    internal static string BeneathRoutePrefix(string routePrefix)
    {
        ArgumentNullException.ThrowIfNull(routePrefix);

        return WellKnownSegment + routePrefix.TrimEnd('/');
    }
}
