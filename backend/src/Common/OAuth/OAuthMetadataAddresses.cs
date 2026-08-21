// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Common.OAuth;

/// <summary>Where an authorization server's metadata is looked for, given only its issuer.</summary>
/// <remarks>
/// <para>
/// A resource server needs two things from an authorization server: the issuer it will compare a token's <c>iss</c>
/// against, and the key set it will check a signature against. Both come out of a discovery document, and no endpoint is
/// ever assembled by hand — a hard-coded <c>/jwks</c> or <c>/token</c> would be a guess about one server's layout that
/// happens to hold until it does not.
/// </para>
/// <para>
/// What is not settled is where that document lives, because two specifications place it differently. OAuth 2.0
/// Authorization Server Metadata inserts its well-known segment between the issuer's authority and its path, while
/// OpenID Connect Discovery historically appends it to the whole issuer, and a server may publish either or both. The
/// MCP authorization specification resolves this by naming an order to try rather than a single location, and that order
/// is what this produces: the OAuth form first, then the OpenID Connect form with the same path insertion, then the
/// appended form that older OpenID providers serve.
/// </para>
/// <para>
/// Every candidate is derived from the configured issuer and therefore reaches only the server the profile already
/// names. Nothing a token carries influences any of them, which is what keeps discovery from becoming a way to make the
/// host fetch an address chosen by whoever sent a request.
/// </para>
/// </remarks>
public static class OAuthMetadataAddresses
{
    private const string OAuthWellKnownSegment = "/.well-known/oauth-authorization-server";

    private const string OpenIdConnectWellKnownSegment = "/.well-known/openid-configuration";

    /// <summary>Reports where the discovery document for an issuer is looked for, in the order the MCP authorization specification names.</summary>
    /// <param name="issuer">The authorization server's issuer identifier.</param>
    /// <returns>The candidate addresses, most specific specification first.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="issuer" /> is not usable as an OAuth identifier.</exception>
    /// <remarks>
    /// An issuer with no path yields two candidates, because the two specifications place the document identically once
    /// there is no path to insert it before. An issuer with a path yields three: the two insertion forms and the appended
    /// form. A trailing slash on the issuer is not a path, so it produces the two-candidate list rather than a third
    /// address containing a doubled separator.
    /// </remarks>
    public static IReadOnlyList<string> ForIssuer(string? issuer)
    {
        if (!OAuthIdentifierUri.IsWellFormed(issuer) || !Uri.TryCreate(issuer.Trim(), UriKind.Absolute, out var parsedIssuer))
        {
            throw new ArgumentException(
                "The candidate metadata addresses were composed before the issuer was validated.",
                nameof(issuer));
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
