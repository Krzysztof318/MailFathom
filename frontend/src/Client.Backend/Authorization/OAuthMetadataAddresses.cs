// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Authorization;

/// <summary>Where an authorization server's metadata is looked for, given only its issuer.</summary>
/// <remarks>
/// <para>
/// No endpoint is ever assembled by hand. A hard-coded <c>/authorize</c> or <c>/token</c> would be a guess about one
/// server's layout that happens to hold until it does not, and the person it would fail for is somebody signing in to
/// their own deployment against their own identity provider.
/// </para>
/// <para>
/// What is not settled is where the document lives, because two specifications place it differently. OAuth 2.0
/// Authorization Server Metadata inserts its well-known segment between the issuer's authority and its path, while
/// OpenID Connect Discovery historically appends it to the whole issuer, and a server may publish either or both. The
/// MCP authorization specification resolves this by naming an order to try rather than a single location, and that
/// order is what this produces — the same order the service itself uses, stated again at this end because nothing
/// under <c>frontend/</c> references a backend assembly.
/// </para>
/// <para>
/// Every candidate is derived from the issuer the deployment named, so the search reaches only the server the
/// deployment already trusts. Nothing a redirect carries influences any of them.
/// </para>
/// </remarks>
internal static class OAuthMetadataAddresses
{
    private const string OAuthWellKnownSegment = "/.well-known/oauth-authorization-server";

    private const string OpenIdConnectWellKnownSegment = "/.well-known/openid-configuration";

    /// <summary>Reports where the discovery document for an issuer is looked for, in the order the MCP authorization specification names.</summary>
    /// <param name="issuer">The authorization server's issuer identifier.</param>
    /// <returns>The candidate addresses, most specific specification first, or an empty list when the issuer is not usable as one.</returns>
    /// <remarks>
    /// An issuer with no path yields two candidates, because the two specifications place the document identically once
    /// there is no path to insert it before. An issuer with a path yields three: the two insertion forms and the
    /// appended form. A trailing slash is not a path, so it produces the two-candidate list rather than a third address
    /// containing a doubled separator.
    /// </remarks>
    internal static IReadOnlyList<string> ForIssuer(string? issuer)
    {
        if (!Uri.TryCreate(issuer?.Trim(), UriKind.Absolute, out var parsedIssuer)
            || parsedIssuer.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(parsedIssuer.Query)
            || !string.IsNullOrEmpty(parsedIssuer.Fragment))
        {
            return [];
        }

        var origin = parsedIssuer.GetLeftPart(UriPartial.Authority);
        var path = parsedIssuer.AbsolutePath.TrimEnd('/');

        return path.Length == 0
            ?
            [
                origin + OAuthWellKnownSegment,
                origin + OpenIdConnectWellKnownSegment,
            ]
            :
            [
                origin + OAuthWellKnownSegment + path,
                origin + OpenIdConnectWellKnownSegment + path,
                origin + path + OpenIdConnectWellKnownSegment,
            ];
    }
}
