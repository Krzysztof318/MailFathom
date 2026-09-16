// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails;

/// <summary>States what a delete MailFathom authors does to the message on the mail server.</summary>
/// <remarks>
/// <para>
/// It is the server-side half of the decision <see cref="AuthoredDeleteEmailDisposition" /> makes locally, and the two
/// are separate because they answer for different copies: this one decides whether the server still holds the message
/// once the delete is done, and that one decides what MailFathom keeps of it. Every combination is meaningful — a
/// mailbox read by other clients may want the message flagged and recoverable there while MailFathom forgets it.
/// </para>
/// <para>
/// The value is resolved when the mutation is written down and travels on its record, so changing the setting while a
/// delete is in flight governs the deletes authored after the change and leaves that one exactly as it was begun.
/// </para>
/// </remarks>
public enum AuthoredDeleteServerDisposition
{
    /// <summary>Flags the message <c>\Deleted</c> and expunges exactly that message, so the server no longer holds it.</summary>
    /// <remarks>
    /// It is the default because it is what deleting means, and every deletion setting of an account defaults to deleting
    /// completely.
    /// </remarks>
    Expunge = 0,

    /// <summary>Flags the message <c>\Deleted</c> and issues no expunge of any kind.</summary>
    /// <remarks>
    /// The server goes on holding the message until the server itself, another client, or its own retention expunges
    /// it, and until then removing the flag there undoes the delete. Synchronization follows the occurrence for exactly
    /// that reason: an expunge somebody else issues later is recorded against the delete, and a flag somebody removes
    /// brings the message back.
    /// </remarks>
    FlagDeleted = 1,
}
