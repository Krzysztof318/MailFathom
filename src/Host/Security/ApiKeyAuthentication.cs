// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Security;

/// <summary>The names the API key authentication scheme publishes, whichever surface it protects.</summary>
/// <remarks>
/// They are constants rather than settings, and the challenge is what an HTTP client reads: <c>Bearer</c>, because that
/// is the scheme the credential is presented under and the one a client already knows how to answer. The scheme's own
/// name is not among them, because it differs per surface and is composed by
/// <see cref="TransportSurface.ApiKeySchemeName" />.
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
}
