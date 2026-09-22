// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Mutations;

/// <summary>States what one recorded mutation has to do with MailFathom's own copy of the message.</summary>
/// <remarks>
/// <para>
/// A record says what a mail server is to be told. On an account whose mailbox MailFathom holds, and on one being
/// restored to its source, the same change also reaches the stored message — and when it does is the whole of what
/// this states: not at all, later, or already. The three are exclusive, which is why they are one value rather than a
/// flag each: a record is opened as exactly one of them and no act produces two.
/// </para>
/// <para>
/// It is written once with the row and never rewritten, because it says what the record was opened as rather than what
/// the account is now. A record outlives the phase it was opened under — an account held again after a restore holds
/// records of all three kinds — so a pass reading the phase instead would act on the wrong one.
/// </para>
/// <para>
/// The value is stored as its name, for the reason
/// <see cref="MailboxMutationStage" /> is: it stays readable in an ad-hoc audit query and survives any later reordering
/// of this enum.
/// </para>
/// </remarks>
public enum MailboxMutationLocalChange
{
    /// <summary>Nothing happens locally, now or later: the record names a change a mail server is to be told about.</summary>
    /// <remarks>Every record a mirrored account opens is this, and so is the one a restoring account opens for a message the drain has not yet reached.</remarks>
    None = 0,

    /// <summary>The record erases MailFathom's own copy once its window has passed, and reaches no mail server at all.</summary>
    /// <remarks>
    /// The second delete of a message already in the local trash, on an account holding or restoring its mailbox. It is
    /// the one act that is local in every phase, and the local copy is still there while the record waits, which is what
    /// leaves the person their window.
    /// </remarks>
    Erasure = 1,

    /// <summary>The change is already committed on the stored message, and the record exists to carry it to the source.</summary>
    /// <remarks>
    /// What a restoring account opens beside its local commit, in the same transaction. Nothing withdraws such a record:
    /// withdrawal is a statement about a change that has not happened, and cancelling this one would leave the stored
    /// message changed with nothing left to tell the source about it.
    /// </remarks>
    AlreadyCommitted = 2,
}
