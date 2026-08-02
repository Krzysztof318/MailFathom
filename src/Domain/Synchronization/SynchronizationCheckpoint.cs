// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Synchronization;

/// <summary>Tracks how far local synchronization has durably processed one folder UIDVALIDITY scope.</summary>
public sealed record SynchronizationCheckpoint(ImapUidValidity UidValidity, ImapUid? LastSeenUid, DateTimeOffset? SynchronizedAt)
{
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
}
