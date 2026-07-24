// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Messages;

namespace MailMcp.Domain.Synchronization;

/// <summary>Tracks how far local synchronization has durably processed one folder UIDVALIDITY scope.</summary>
public sealed record SynchronizationCheckpoint(ImapUidValidity UidValidity, ImapUid? LastSeenUid, DateTimeOffset? SynchronizedAt)
{
    /// <summary>Creates an empty checkpoint for a UIDVALIDITY scope.</summary>
    /// <param name="uidValidity">The folder UIDVALIDITY value.</param>
    /// <returns>An empty checkpoint.</returns>
    public static SynchronizationCheckpoint None(ImapUidValidity uidValidity) => new(uidValidity, null, null);

    /// <summary>Advances the checkpoint after a message has been durably stored.</summary>
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
