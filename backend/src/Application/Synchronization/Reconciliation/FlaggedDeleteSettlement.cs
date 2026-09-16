// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Synchronization.Reconciliation;

/// <summary>A delete that left its message flagged, seen flagged for the first time, and the local disposition it was authored under.</summary>
/// <param name="StoredEmailId">The local email the delete was about.</param>
/// <param name="LocalDisposition">What becomes of it now, read off the delete's record rather than the account's configuration.</param>
public sealed record SettledFlaggedDelete(StoredEmailId StoredEmailId, AuthoredDeleteEmailDisposition LocalDisposition);

/// <summary>Everything one follow-up of a folder's flag-only deletes learned, as one thing to apply.</summary>
/// <param name="Settled">
/// The deletes whose flag was seen for the first time. Each has its local copy disposed of and its occurrence followed
/// from then on: a kept row is taken out of every mailbox query unless the disposition keeps it readable, and an erased
/// one is removed with everything derived from it.
/// </param>
/// <param name="StillFlagged">The followed occurrences the server still holds flagged, which only move to the back of the queue.</param>
/// <param name="Expunged">
/// The followed occurrences the server no longer holds. The expunge is recorded on a kept row and the record is retired;
/// the local copy stays exactly as the delete left it.
/// </param>
/// <param name="Restored">
/// The followed occurrences whose kept row comes back as live mail because the flag was removed on the server. An
/// occurrence whose local copy was erased is never in this list: its message has to be stored again first, and the
/// caller retires its record once it has been.
/// </param>
/// <param name="ObservedAt">When the server was read.</param>
public sealed record FlaggedDeleteSettlement(
    IReadOnlyList<SettledFlaggedDelete> Settled,
    IReadOnlyList<DeleteLeftFlagged> StillFlagged,
    IReadOnlyList<DeleteLeftFlagged> Expunged,
    IReadOnlyList<DeleteLeftFlagged> Restored,
    DateTimeOffset ObservedAt);
