// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Accounts;

/// <summary>What one mail account's custody was last asked to be, and how far the work of granting it has got.</summary>
/// <param name="Requested">The custody an administrator asked for.</param>
/// <param name="Phase">Which copy of the mailbox is the truth at this moment.</param>
/// <remarks>
/// The pair is read together everywhere a decision turns on custody, because neither value answers on its own: an
/// account asked to hold its mailbox is still mirrored until its outstanding remote mutations have been carried, and
/// one asked to mirror again is still the truth for as long as the restore is appending. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </remarks>
public sealed record MailAccountCustodyState(MailAccountCustody Requested, MailAccountCustodyPhase Phase)
{
    /// <summary>Gets which restore of this account is the current one, counted from one and zero for an account that has never restored.</summary>
    /// <remarks>
    /// It advances once each time the account enters <see cref="MailAccountCustodyPhase.Restoring" />, and it is what
    /// tells one restore's work apart from an earlier one's. The restore writes a held message's state down as ordinary
    /// mutation records, whose identity is the occurrence, the requester, and the mutation together, and a completed
    /// record is kept for good — so without this a second hold-and-restore cycle would find the first cycle's records
    /// already standing for every message nobody moved, carry none of the second hold's read, star, or label state, and
    /// end the phase saying it had.
    /// </remarks>
    public int RestoreGeneration { get; init; }

    /// <summary>The state of an account nobody has switched, which is what a deployment reads for every account it holds.</summary>
    public static MailAccountCustodyState Mirrored { get; } =
        new(MailAccountCustody.MirrorSource, MailAccountCustodyPhase.Mirrored);

    /// <summary>Gets whether MailFathom rather than the source server is the truth about this mailbox.</summary>
    /// <remarks>
    /// True in both phases the switch passes through, because a restoring account is still answering from stored state
    /// while it appends the mailbox back: the source becomes the truth only once the phase is
    /// <see cref="MailAccountCustodyPhase.Mirrored" /> again.
    /// </remarks>
    public bool IsMailFathomTheTruth => this.Phase is not MailAccountCustodyPhase.Mirrored;

    /// <summary>Gets whether a disappearance from the source is a deletion this account applies to what it stores.</summary>
    /// <remarks>
    /// <para>
    /// It is not, from the moment MailFathom becomes the truth, because MailFathom is the one emptying the source: a
    /// message that has left is one the drain removed, and applying the account's disposition for mail somebody else
    /// deleted would erase or tombstone every message the mode successfully drained.
    /// </para>
    /// <para>
    /// The answer is the phase's alone and never the account's configured disposition, which is why the switch no
    /// longer refuses an account configured to erase the local copy: the configuration goes on saying what it says and
    /// stops being consulted for as long as the mailbox is held.
    /// </para>
    /// </remarks>
    public bool AppliesRemoteDeletions => !this.IsMailFathomTheTruth;

    /// <summary>Gets whether the account is being asked to move to a custody it has not reached yet.</summary>
    public bool IsSwitchPending => this.Requested switch
    {
        MailAccountCustody.HoldMailbox => this.Phase is not MailAccountCustodyPhase.Held,
        _ => this.Phase is not MailAccountCustodyPhase.Mirrored,
    };
}
