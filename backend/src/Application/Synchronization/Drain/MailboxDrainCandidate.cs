// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Synchronization.Drain;

/// <summary>One stored message of a held account whose source server still holds an occurrence of it.</summary>
/// <param name="Email">The stored message, by the identity that outlives the occurrence.</param>
/// <param name="Occurrence">Where the source still holds it, which is the UID the drain would expunge.</param>
/// <param name="Folder">The binding the occurrence belongs to, including the remote path a write session selects.</param>
/// <param name="ContentAvailability">What the row says about whether the payload is stored.</param>
/// <param name="ContentVerifiedAt">When the payload was last read back and matched what the row records, or <see langword="null" /> while it never has been.</param>
/// <remarks>
/// The binding travels with the candidate for the reason it travels with an outstanding mutation: the occurrence names
/// a folder without saying where it is, and the drain needs both — the path to select, and the alias to report.
/// </remarks>
public sealed record MailboxDrainCandidate(
    StoredEmailId Email,
    EmailOccurrenceId Occurrence,
    MailFolderResolution Folder,
    StoredEmailContentAvailability ContentAvailability,
    DateTimeOffset? ContentVerifiedAt)
{
    /// <summary>Judges the message on what the row alone says, before any payload is read.</summary>
    /// <returns>What holds the message on its source, or <see cref="MailboxDrainHoldBack.None" /> when only the read-back is left to do.</returns>
    /// <remarks>
    /// The cheap half of the gate, asked first so a message whose payload was never stored costs no read at all. It is
    /// deliberately a closed reading of the availability rather than a comparison against the one value that means
    /// stored: a later value meaning the bytes are absent must hold the message back by default rather than by having
    /// been remembered here.
    /// </remarks>
    public MailboxDrainHoldBack FindHoldBackInStoredState() => this.ContentAvailability switch
    {
        StoredEmailContentAvailability.Available => MailboxDrainHoldBack.None,
        StoredEmailContentAvailability.ExceededSizeLimit => MailboxDrainHoldBack.ContentAboveSizeLimit,
        StoredEmailContentAvailability.AwaitingStorageHeadroom => MailboxDrainHoldBack.AwaitingStorageHeadroom,
        _ => MailboxDrainHoldBack.ContentNotStored,
    };
}
