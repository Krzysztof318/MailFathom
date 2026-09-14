// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>States what deleting a folder through the client does on the mail server an account mirrors.</summary>
/// <remarks>
/// <para>
/// It sits beside the account's two email deletion settings and answers for a third act. Those two govern what becomes
/// of one message; this governs what becomes of a folder and everything the server holds in it, which is a loss of a
/// different size and therefore a decision of its own rather than a value inherited from either.
/// </para>
/// <para>
/// It governs only a deletion the mailbox user authored through the client. Nothing else in MailFathom deletes a
/// folder, so there is no counterpart here to the disappearance-observed setting mail has, and an account whose source
/// loses a folder on its own is unaffected by whatever this says.
/// </para>
/// <para>
/// See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>,
/// axis M.
/// </para>
/// </remarks>
public enum AuthoredFolderDeleteDisposition
{
    /// <summary>Deletes the folder on the mail server and removes the mail MailFathom stored from it.</summary>
    /// <remarks>
    /// The folder goes on both sides, which is what a person deleting a folder in a mail client means by the gesture.
    /// It is the default because every other deletion setting of an account is becoming full deletion, and because a
    /// deletion that leaves the folder on the server brings its mail back the moment the alias is declared again.
    /// </remarks>
    DeleteOnServer = 0,

    /// <summary>Leaves the folder and its mail on the mail server, and marks the folder deleted in MailFathom alone.</summary>
    /// <remarks>
    /// Nothing reaches the server. The folder leaves the account's tree and leaves what the account synchronizes, and
    /// the mail stored from it is excluded from every mailbox query exactly as a tombstoned message is — a folder being
    /// readable by being declared rather than by not being mentioned. It is the value for a deployment whose copy must
    /// never outlive its source's, and the one that destroys nothing.
    /// </remarks>
    MarkDeletedLocally = 1,
}
