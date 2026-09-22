// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Synchronization.Restore;

/// <summary>One message of a restoring account that its source still holds, whose stored state has to reach it.</summary>
/// <param name="Email">The stored message, by the identity that outlives an occurrence.</param>
/// <param name="Occurrence">Where the source holds it, which is what every record the restore writes is aimed at.</param>
/// <param name="Folder">The binding the occurrence belongs to, which is where the message is now.</param>
/// <param name="DestinationAlias">The alias of the folder the message belongs in, from the local folder it was moved into.</param>
/// <param name="State">The flags and keywords MailFathom holds, which the records write onto the occurrence.</param>
/// <remarks>
/// These are the messages the drain never reached. The source still has them where it always did, carrying whatever
/// flags it last observed before the switch on — so the records are issued whatever the source now says, because its
/// own values stopped being the truth at that moment.
/// </remarks>
public sealed record MailboxRestoredStateCandidate(
    StoredEmailId Email,
    EmailOccurrenceId Occurrence,
    MailFolderResolution Folder,
    MailFolderAlias DestinationAlias,
    RestoredEmailState State)
{
    /// <summary>Gets whether the message has to be moved on the source before its state is written.</summary>
    /// <remarks>
    /// A move the mailbox's user made while the account was held reaches the source as an ordinary relocation, because
    /// the occurrence is in the folder it was drained from rather than the one the message is in now.
    /// </remarks>
    public bool IsMisplaced => this.Folder.Alias != this.DestinationAlias;
}
