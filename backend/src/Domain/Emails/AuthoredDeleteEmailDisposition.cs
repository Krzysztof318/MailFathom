// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails;

/// <summary>States what becomes of the local copy of an email MailFathom itself deleted on the mail server.</summary>
/// <remarks>
/// <para>
/// It is a separate decision from <see cref="RemotelyDeletedEmailDisposition" /> because the two answer for different
/// acts. That one governs a disappearance somebody else caused and MailFathom observed afterwards; this one governs a
/// deletion the mailbox owner authored through MailFathom. Inheriting one value for both would make an account
/// configured to erase what its server loses also erase what MailFathom was just told to delete — which is precisely the
/// case where the owner may have wanted the opposite, because deleting on the server is how mail is kept while quota is
/// freed.
/// </para>
/// <para>
/// The value is resolved when the mutation is written down and travels on its record, so changing the setting while a
/// delete is in flight governs the deletes authored after the change and leaves that one exactly as it was begun.
/// </para>
/// </remarks>
public enum AuthoredDeleteEmailDisposition
{
    /// <summary>Keeps the local email readable and searchable although the server no longer holds it.</summary>
    /// <remarks>
    /// The remote occurrence is gone and the local copy is not, which is what separates freeing space on the server from
    /// forgetting the mail. Nothing is destroyed, so it is the default for the same reason
    /// <see cref="RemotelyDeletedEmailDisposition.RetainTombstone" /> is: a disposition an operator has not thought about
    /// must not be the reason mail stops being readable.
    /// </remarks>
    RetainLocalCopy = 0,

    /// <summary>Keeps the local row as a tombstone that every mailbox query excludes.</summary>
    /// <remarks>
    /// The record that the email existed survives and the mail itself stops being reachable, which is what makes an
    /// authored delete auditable without leaving the message readable. It is the exact counterpart of
    /// <see cref="RemotelyDeletedEmailDisposition.RetainTombstone" />.
    /// </remarks>
    RetainTombstone = 1,

    /// <summary>Removes the local row together with its raw MIME and everything derived from it.</summary>
    /// <remarks>
    /// Nothing of the email survives locally, so a deployment that must not retain mail past the server's own copy can
    /// say so for its own deletions as well as for the server's. The removal happens as the disappearance is observed,
    /// which is deliberate and irreversible.
    /// </remarks>
    EraseLocalCopy = 2,
}
