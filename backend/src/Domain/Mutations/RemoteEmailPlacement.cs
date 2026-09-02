// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Mutations;

/// <summary>States where a mutation left an email in the folder it put it into, when the server said.</summary>
/// <remarks>
/// <para>
/// A message that arrives in another folder is a different occurrence with a different UID, and only RFC 4315's
/// <c>COPYUID</c> response names it directly. A server advertising no <c>UIDPLUS</c> completes the same change and
/// simply does not say where, which is why the absence is a value this type carries rather than a failure or a
/// <see langword="null" /> a caller has to interpret: the alternative is searching the destination folder for the
/// message afterwards, and that is a guess about identity rather than a fact the server gave.
/// </para>
/// <para>
/// The UIDVALIDITY travels with the UID because a UID is only stable inside one. Reporting the number alone would let a
/// destination folder recreated between two mutations hand back an identity naming a different email.
/// </para>
/// </remarks>
public sealed record RemoteEmailPlacement
{
    private RemoteEmailPlacement(ImapUidValidity? uidValidity, ImapUid? uid)
    {
        this.UidValidity = uidValidity;
        this.Uid = uid;
    }

    /// <summary>Gets the UIDVALIDITY of the folder the email was put into, or <see langword="null" /> when the server named neither.</summary>
    public ImapUidValidity? UidValidity { get; }

    /// <summary>Gets the UID the email was assigned there, or <see langword="null" /> when the server named neither.</summary>
    public ImapUid? Uid { get; }

    /// <summary>Gets whether the server named where it put the email.</summary>
    public bool IsReported => this.Uid is not null;

    /// <summary>Reports the identity a <c>COPYUID</c> response named.</summary>
    /// <param name="uidValidity">The UIDVALIDITY the destination folder reported.</param>
    /// <param name="uid">The UID the email was assigned in that folder.</param>
    /// <returns>A placement naming the new occurrence.</returns>
    public static RemoteEmailPlacement Reported(ImapUidValidity uidValidity, ImapUid uid) => new(uidValidity, uid);

    /// <summary>Reports a server that completed the change without naming where the email landed.</summary>
    /// <returns>A placement naming no occurrence.</returns>
    public static RemoteEmailPlacement NotReported() => new(null, null);
}
