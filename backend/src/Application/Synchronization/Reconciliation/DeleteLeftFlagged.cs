// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Synchronization.Reconciliation;

/// <summary>An occurrence a delete MailFathom authored left flagged <c>\Deleted</c> on its server, and is still followed.</summary>
/// <param name="Id">What the record is retired by once the server has expunged the message or the flag was removed.</param>
/// <param name="Uid">The UID asked about, within the folder and UIDVALIDITY the record was read for.</param>
/// <param name="KeptEmail">
/// The local email the delete kept, readable or as a tombstone, and <see langword="null" /> where it erased the local
/// copy — in which case removing the flag on the server is answered by storing the message again.
/// </param>
/// <remarks>
/// It carries the server's name for a position and MailFathom's own identity for a row, and nothing of the message,
/// which is what lets it outlive an erased local copy without being a second copy of it.
/// </remarks>
public sealed record DeleteLeftFlagged(DeleteLeftFlaggedId Id, ImapUid Uid, StoredEmailId? KeptEmail);

/// <summary>Identifies one followed occurrence a delete left flagged.</summary>
/// <param name="Value">The generated identifier.</param>
public readonly record struct DeleteLeftFlaggedId(Guid Value);
