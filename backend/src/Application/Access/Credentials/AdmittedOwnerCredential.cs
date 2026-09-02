// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Credentials;

/// <summary>What judging an owner-facing credential establishes, whichever method was judged.</summary>
/// <param name="CredentialId">The credential that matched, which is what an audit record and a diagnostic correlate on.</param>
/// <param name="Owner">The owner the request acts for.</param>
/// <param name="Permissions">What the request may do, in the published order.</param>
/// <remarks>
/// <para>
/// The three facts are one shape because they are established together and travel together: a credential resolves an
/// owner, and what that owner's caller may do was decided when the credential was provisioned. Four methods producing
/// four shapes of the same answer would be four places for one of the three to be dropped on the way to the principal.
/// </para>
/// <para>
/// What is deliberately absent is the lookup. A username, a key digest, a fingerprint, and a subject are each what the
/// caller presented, and nothing downstream of authentication has any use for one — so a claim, a log line, and an
/// audit record name the identifier instead.
/// </para>
/// </remarks>
public sealed record AdmittedOwnerCredential(
    Guid CredentialId,
    MailOwnerId Owner,
    IReadOnlyList<MailFathomPermission> Permissions)
{
    /// <summary>Describes the credential one resolution admitted, refusing an answer that names nothing.</summary>
    /// <param name="credential">The credential the store resolved.</param>
    /// <returns>What the request was admitted as.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="credential" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the resolved credential names no owner, which is a row the store could not have written.</exception>
    public static AdmittedOwnerCredential For(ResolvedOwnerCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        return credential.Owner.IsSpecified
            ? new AdmittedOwnerCredential(credential.Id, credential.Owner, credential.Permissions)
            : throw new ArgumentException(
                "An admitted credential names the owner the request acts for.",
                nameof(credential));
    }
}
