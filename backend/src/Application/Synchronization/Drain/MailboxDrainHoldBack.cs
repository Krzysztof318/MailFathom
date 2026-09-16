// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization.Drain;

/// <summary>Names why the drain gate kept one message on its source server.</summary>
/// <remarks>
/// A held-back message is an ordinary outcome rather than a failure: the gate exists to keep mail on the source until
/// MailFathom verifiably holds it, so every value here is the gate working. They are reported apart from each other
/// because the futures differ — one clears when an operator raises a limit, one when the ceiling has headroom again,
/// and two are storage defects found while the source still has the mail. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </remarks>
public enum MailboxDrainHoldBack
{
    /// <summary>Nothing held the message back, and the drain may remove it from its source.</summary>
    None = 0,

    /// <summary>The message is above the configured per-message size limit, so its payload was never stored.</summary>
    /// <remarks>
    /// It will be above the limit on every later run, so nothing is waiting for it and the source keeps it. An operator
    /// who wants it drained raises the limit, which is what fetches the payload.
    /// </remarks>
    ContentAboveSizeLimit = 1,

    /// <summary>Local content storage had reached its ceiling when the message arrived, so its payload is not stored yet.</summary>
    /// <remarks>The refill pass stores it once the ceiling has headroom, and the drain takes it then. The source keeps it for as long as the ceiling does, which is what the ceiling is for.</remarks>
    AwaitingStorageHeadroom = 2,

    /// <summary>The store holds no payload for the message at all.</summary>
    ContentNotStored = 3,

    /// <summary>The stored payload does not match the length and digest recorded for it.</summary>
    /// <remarks>
    /// One of the two values here that is a defect rather than a bound, and the whole reason the gate reads the payload
    /// back instead of trusting that a committed row points at a readable one: the source still holds the message, so
    /// the damage is recoverable for exactly as long as the drain refuses to remove it.
    /// </remarks>
    ContentDoesNotMatchRecord = 4,

    /// <summary>The payload was served from the copy the database retained, because the object could not be vouched for.</summary>
    /// <remarks>
    /// The bytes read back are intact, which is why every other reader answers with them — but they are the copy an
    /// operator is one decision away from releasing, and the authoritative object behind them could not be read or did
    /// not match. Draining on that reading would leave the deployment holding nothing after the release and the source
    /// holding nothing either. The gate records the repair the other readers record and keeps the message where it is
    /// until the object answers for itself.
    /// </remarks>
    ContentObjectUnreadable = 5,
}
