// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Security.ApiKeys;
using MailFathom.Infrastructure.Security.Passwords;

namespace MailFathom.Host.Security.Basic;

/// <summary>The names the Basic authentication scheme publishes, and the challenge composed from them.</summary>
/// <remarks>
/// <para>
/// The scheme's own name is not among them, because it differs per surface and is composed by
/// <see cref="Transport.TransportSurface.BasicSchemeName" />. The realm is not here either: it is
/// <see cref="ApiKeyAuthentication.Realm" />, one realm across every surface and every method, because a realm tells a
/// client which credential to reuse where and the surfaces here are told apart by the address a client is configured
/// with.
/// </para>
/// <para>
/// The challenge is the one place this method differs from the others, and it has to. Every other credential this
/// deployment accepts is set deliberately by a client that already holds it, so a bare <c>Bearer</c> challenge is all
/// they need; a username and password are typed by a person whose client only asks for them when a
/// <c>WWW-Authenticate: Basic</c> challenge tells it to. So a surface accepting Basic answers with both challenges,
/// as one header carrying two values — which is what RFC 7235 says a server offering two schemes does, and what keeps
/// an OAuth client's discovery working on an endpoint that also accepts a password.
/// </para>
/// <para>
/// Nothing in the challenge says whether a username exists, whether one was presented, or which of the two halves was
/// wrong. It is the same header on every refusal on the surface: no credential at all, a malformed one, an unknown
/// username, a wrong password, a disabled credential, and a caller that has spent its attempts each receive exactly
/// this.
/// </para>
/// </remarks>
internal static class BasicAuthentication
{
    /// <summary>The claim type carrying the identifier of the credential a request authenticated with.</summary>
    /// <remarks>
    /// The identifier rather than the username, and the difference is what a record is allowed to hold. The username is
    /// the half of a credential that travels beside the password and is typed by a person; the identifier is
    /// MailFathom's own handle for the row, means nothing outside this deployment, and is what an audit record and a
    /// rate-limiting partition already name. A diagnostic that renders a principal therefore names a credential without
    /// naming a way to sign in.
    /// </remarks>
    internal const string CredentialIdClaimType = "urn:mailfathom:owner-credential-id";

    /// <summary>The claim type a role check reads on a password identity, which nothing ever issues.</summary>
    /// <remarks>Named rather than left empty for the reason <see cref="ApiKeyAuthentication.RoleClaimType" /> is: an identity given an empty role type silently reverts to the framework's default.</remarks>
    internal const string RoleClaimType = "urn:mailfathom:owner-credential-role";

    /// <summary>The challenge naming the password method, with the encoding a modern client should use for it.</summary>
    /// <remarks>
    /// The <c>charset</c> parameter is the one RFC 7617 defines and the only value it permits, and it is what makes a
    /// password containing anything outside US-ASCII survive the round trip: without it a client is left to guess an
    /// encoding, and two clients guessing differently would send two different credentials for one typed password.
    /// </remarks>
    private const string PasswordChallenge =
        $"{BasicCredentialHeader.HttpAuthenticationScheme} realm=\"{ApiKeyAuthentication.Realm}\", charset=\"UTF-8\"";

    /// <summary>Answers a request with the <c>401</c> a surface accepting passwords produces.</summary>
    /// <param name="response">The response to write the refusal onto.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The bare bearer challenge every method on the surface produces, and the password challenge beside it, as two
    /// values of one header. The bearer half is written by the method that owns it rather than restated here, so a
    /// surface accepting both offers exactly what it would have offered without this one.
    /// </remarks>
    internal static void WriteChallenge(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        ApiKeyAuthentication.WriteBareChallenge(response);

        response.Headers.WWWAuthenticate = response.Headers.WWWAuthenticate.Append(PasswordChallenge).ToArray();
    }
}
