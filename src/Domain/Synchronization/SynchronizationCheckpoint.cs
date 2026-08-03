// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Synchronization;

/// <summary>Tracks how far local synchronization has durably processed one folder UIDVALIDITY scope.</summary>
public sealed record SynchronizationCheckpoint(ImapUidValidity UidValidity, ImapUid? LastSeenUid, DateTimeOffset? SynchronizedAt)
{
    /// <summary>Gets the folder modification sequence the backward pass has reconciled the whole folder through.</summary>
    /// <remarks>
    /// <para>
    /// It is absent for a folder whose server does not report modification sequences, and absent until a backward pass
    /// has covered every occurrence the folder holds in one run. Recording it after a partial pass would claim that
    /// everything older than that sequence is already accounted for, and the occurrences the pass never reached would
    /// then never be asked about again.
    /// </para>
    /// <para>
    /// It belongs to the same UIDVALIDITY scope as the rest of the checkpoint. A renumbered folder is a different UID
    /// space, so the sequence recorded under the previous one describes messages the current one says nothing about and
    /// is dropped with the rest of the progress rather than carried across.
    /// </para>
    /// </remarks>
    public ulong? ReconciledThroughModSeq { get; init; }

    /// <summary>Creates an empty checkpoint for a UIDVALIDITY scope.</summary>
    /// <param name="uidValidity">The folder UIDVALIDITY value.</param>
    /// <returns>An empty checkpoint.</returns>
    public static SynchronizationCheckpoint None(ImapUidValidity uidValidity) => new(uidValidity, null, null);

    /// <summary>Determines whether another checkpoint identifies the same durable mailbox progress.</summary>
    /// <param name="other">The checkpoint to compare.</param>
    /// <returns>
    /// <see langword="true" /> when both checkpoints have the same UIDVALIDITY and last-seen UID; otherwise,
    /// <see langword="false" />.
    /// </returns>
    /// <remarks>
    /// The reconciled modification sequence is deliberately not compared. It is an optimization hint whose worst
    /// outcome when lost is one full window scan, while the UID progress it sits beside decides which mail is fetched
    /// at all, so widening the compare would turn a harmless race into a refused advance.
    /// </remarks>
    public bool RepresentsSameProgressAs(SynchronizationCheckpoint? other) =>
        other is not null
        && this.UidValidity == other.UidValidity
        && this.LastSeenUid == other.LastSeenUid;

    /// <summary>Advances the checkpoint after an email has been durably stored.</summary>
    /// <param name="uid">The latest stored UID.</param>
    /// <param name="synchronizedAt">The timestamp for the durable synchronization progress.</param>
    /// <returns>An advanced checkpoint.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="uid" /> is older than the current checkpoint.</exception>
    public SynchronizationCheckpoint AdvanceTo(ImapUid uid, DateTimeOffset synchronizedAt)
    {
        if (this.LastSeenUid is { } lastSeenUid && uid.Value < lastSeenUid.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(uid), "Synchronization checkpoints cannot move backwards within the same UIDVALIDITY scope.");
        }

        return this with { LastSeenUid = uid, SynchronizedAt = synchronizedAt };
    }

    /// <summary>Records that the backward pass has covered the whole folder as of one modification sequence.</summary>
    /// <param name="modSeq">The folder modification sequence the completed pass was read under.</param>
    /// <returns>A checkpoint carrying the sequence, or this one when it already accounts for a later pass.</returns>
    /// <remarks>
    /// A sequence older than the one already recorded is ignored rather than refused. Two runs can complete a pass over
    /// the same folder, and the one that finishes second may have read the folder first; keeping the later sequence
    /// costs the earlier run nothing, because everything it observed is covered by the sequence that is kept.
    /// </remarks>
    public SynchronizationCheckpoint ReconciledThrough(ulong modSeq) =>
        this.ReconciledThroughModSeq is { } recordedModSeq && recordedModSeq >= modSeq
            ? this
            : this with { ReconciledThroughModSeq = modSeq };
}
