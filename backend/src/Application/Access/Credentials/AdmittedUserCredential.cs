// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Credentials;

/// <summary>What judging a user-facing credential establishes, whichever method was judged.</summary>
/// <param name="CredentialId">The credential that matched, which is what an audit record and a diagnostic correlate on.</param>
/// <param name="User">The user the request acts for.</param>
/// <param name="Permissions">The names the request is kept to, in the published order, which narrow what its user's roles grant rather than granting anything.</param>
/// <param name="EndpointAccess">Which endpoints the user may be served on, as read when the credential or the session was resolved, which the surface judging the request asks of its own switch.</param>
/// <remarks>
/// <para>
/// The four facts are one shape because they are established together and travel together: a credential resolves a
/// user, the narrowing its caller is kept to was decided when the credential was provisioned, and which endpoints the user
/// is served on is read in the same statement. Four methods producing four shapes of the same answer would be four
/// places for one of them to be dropped on the way to the principal.
/// </para>
/// <para>
/// What is deliberately absent is the lookup. A username, a key digest, a fingerprint, and a subject are each what the
/// caller presented, and nothing downstream of authentication has any use for one — so a claim, a log line, and an
/// audit record name the identifier instead.
/// </para>
/// </remarks>
public sealed record AdmittedUserCredential(
    Guid CredentialId,
    UserId User,
    IReadOnlyList<MailFathomPermission> Permissions,
    UserEndpointAccess EndpointAccess)
{
    /// <summary>Gets where the credential may be presented, which the surface judging the request asks of it.</summary>
    /// <remarks>A session exchanged for a credential carries the default surfaces, because it is judged by the surface it was minted on rather than by where its credential may be presented, and the credential's source networks, which hold on every request the session admits.</remarks>
    public UserCredentialReach Reach { get; init; } = UserCredentialReach.Default;

    /// <summary>Describes the credential one resolution admitted, refusing an answer that names nothing.</summary>
    /// <param name="credential">The credential the store resolved.</param>
    /// <returns>What the request was admitted as.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="credential" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the resolved credential names no user, which is a row the store could not have written.</exception>
    public static AdmittedUserCredential For(ResolvedUserCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        return credential.User.IsSpecified
            ? new AdmittedUserCredential(
                credential.Id,
                credential.User,
                credential.Permissions,
                credential.EndpointAccess)
            {
                Reach = credential.Reach,
            }
            : throw new ArgumentException(
                "An admitted credential names the user the request acts for.",
                nameof(credential));
    }
}
