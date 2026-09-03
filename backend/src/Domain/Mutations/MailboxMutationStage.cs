// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Mutations;

/// <summary>States how far along its protocol sequence one recorded mutation has durably reached.</summary>
/// <remarks>
/// <para>
/// The members are the stages the IMAP sequences actually have rather than a generic pending, running, and done. That
/// is what makes the value usable for resumption: a retry reads the stage and continues from it, and the one command
/// that must never be issued twice is recognized by the stage that precedes it rather than by inspecting the mailbox
/// afterwards, which cannot tell a copy MailFathom made from one a person made.
/// </para>
/// <para>
/// No mutation passes through every stage. A <see cref="MailboxMutation.SetSeen" /> goes straight from
/// <see cref="Recorded" /> to <see cref="Completed" />, because a flag write is idempotent on the wire and its record
/// exists for provenance rather than for retry safety. A <see cref="MailboxMutation.Delete" /> passes through
/// <see cref="SourceFlaggedDeleted" /> and never through the placement stages, because it puts the email nowhere. A
/// <see cref="MailboxMutation.Copy" /> passes through the placement stages and never through
/// <see cref="SourceFlaggedDeleted" />, because it leaves the source alone. Only a
/// <see cref="MailboxMutation.Relocate" /> carried by the fallback sequence reaches all four.
/// </para>
/// <para>
/// The stage is stored as its name so it stays readable in an ad-hoc audit query and survives any later reordering of
/// this enum, which is the same reason the stored content availability and the content defect are stored that way.
/// </para>
/// </remarks>
public enum MailboxMutationStage
{
    /// <summary>The intent is durable and no IMAP command has been issued for it.</summary>
    /// <remarks>Every mutation starts here, and a retry from here is safe because nothing has reached the server.</remarks>
    Recorded = 0,

    /// <summary>
    /// The command that would place the email in its destination folder has gone out, and its answer has not been read.
    /// </summary>
    /// <remarks>
    /// This is the one stage a retry may not act on. <c>UID COPY</c> issued twice is a second message rather than a
    /// repeat of the first, and nothing in the destination folder afterwards says whether the first attempt landed, so a
    /// mutation found here is reported as an unknown outcome and left for a person or for convergence to resolve.
    /// </remarks>
    PlacementIssued = 1,

    /// <summary>The server acknowledged the placement, and named it where it supplied a <c>COPYUID</c> response.</summary>
    /// <remarks>
    /// A copy is finished at this stage. A relocation carried by the fallback sequence is not: its source is still in
    /// the folder, and removing it is what remains.
    /// </remarks>
    PlacementConfirmed = 2,

    /// <summary>The source email carries <c>\Deleted</c> and the message-scoped expunge has not been acknowledged.</summary>
    /// <remarks>Both commands are idempotent for one UID, so a retry from here reissues the expunge alone and a repeat costs nothing.</remarks>
    SourceFlaggedDeleted = 3,

    /// <summary>The mutation is done, and asking for it again performs nothing.</summary>
    Completed = 4,

    /// <summary>The mutation will not be attempted again, and the failure that ended it is on the record.</summary>
    /// <remarks>A refused mutation and one that spent its bounded attempts both end here, which is what keeps a failing change visible instead of pending forever.</remarks>
    Abandoned = 5,

    /// <summary>The change was withdrawn before any command went out, and nothing will attempt it.</summary>
    /// <remarks>
    /// Only a record at <see cref="Recorded" /> reaches here, which is what makes withdrawal a statement about the
    /// mailbox rather than about the record alone: nothing had been asked of the server, so nothing has to be undone.
    /// It is a stage of its own rather than <see cref="Abandoned" /> because the two say opposite things to whoever is
    /// watching — an abandoned change is stuck and waiting for a person, and a withdrawn one is finished and wanted by
    /// nobody.
    /// </remarks>
    Cancelled = 6,
}
