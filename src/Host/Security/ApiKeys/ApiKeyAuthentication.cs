// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Security.Transport;

namespace MailFathom.Host.Security.ApiKeys;

/// <summary>The names the API key authentication scheme publishes, and the challenge composed from them.</summary>
/// <remarks>
/// They are constants rather than settings, and the challenge is what an HTTP client reads: <c>Bearer</c>, because that
/// is the scheme the credential is presented under and the one a client already knows how to answer. The scheme's own
/// name is not among them, because it differs per surface and is composed by
/// <see cref="TransportSurface.ApiKeySchemeName" />.
/// <para>
/// Every credential method on a surface refuses with the same challenge, so it is written here — beside the constants
/// it is composed from — rather than by each handler that has to answer one.
/// </para>
/// </remarks>
internal static class ApiKeyAuthentication
{
    /// <summary>The HTTP authentication scheme a credential is presented under, and the one a challenge names.</summary>
    internal const string HttpAuthenticationScheme = "Bearer";

    /// <summary>The protection space a challenge names, which stays constant so a client can cache a credential against it.</summary>
    /// <remarks>
    /// One realm across every surface, deliberately. A realm tells a client which credential to reuse where, and the
    /// surfaces here are told apart by the address a client is configured with rather than by a name in a challenge, so
    /// publishing two would suggest a distinction no client has any way to act on.
    /// </remarks>
    internal const string Realm = "MailFathom";

    /// <summary>The claim type carrying the name of the API key a request authenticated with.</summary>
    /// <remarks>
    /// A private claim type rather than a registered one: the value is MailFathom's own configuration identity for a
    /// credential, not a subject any other system issued or would recognize. It exists so an audit record and a
    /// diagnostic can name which key was used, and it is the only thing the principal carries.
    /// <para>
    /// One claim type across every surface, because it says what kind of credential authenticated rather than where. A
    /// principal never crosses a surface: each registers its own routing scheme and each endpoint requires a policy
    /// naming only that scheme, so which surface's keys were consulted is settled before this claim is ever read.
    /// </para>
    /// </remarks>
    internal const string ApiKeyNameClaimType = "urn:mailfathom:api-key-name";

    /// <summary>The claim type a role check reads on a key's identity, which nothing ever issues.</summary>
    /// <remarks>Named rather than left empty, because an identity given an empty role type silently reverts to the framework's default; a claim type no mapping writes is what actually makes a role check answer no.</remarks>
    internal const string RoleClaimType = "urn:mailfathom:api-key-role";

    /// <summary>The challenge a refusal answers with, naming the scheme and the protection space and nothing else.</summary>
    /// <remarks>An error code or a description would begin to describe which credential was wrong, which is what makes every refusal on a surface indistinguishable from every other.</remarks>
    private const string BareChallenge = $"{HttpAuthenticationScheme} realm=\"{Realm}\"";

    /// <summary>Answers a request with the empty <c>401</c> every refusal on a surface produces.</summary>
    /// <param name="response">The response to write the refusal onto.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Written here rather than by each scheme's handler because the header is security-visible and a client is told
    /// which protection space to hold a credential for, never which method judged it: two schemes composing the same
    /// string separately is a header that can differ between them for no reason a client could act on.
    /// </remarks>
    internal static void WriteBareChallenge(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.StatusCode = StatusCodes.Status401Unauthorized;
        response.Headers.WWWAuthenticate = BareChallenge;
    }
}
