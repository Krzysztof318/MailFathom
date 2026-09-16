// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Synchronization.Drain;

/// <summary>A message the source still holds that MailFathom erased before the drain reached it.</summary>
/// <param name="Id">What the record is deleted by once the removal is done.</param>
/// <param name="Occurrence">Where the source holds it, which is the UID the drain expunges.</param>
/// <param name="Folder">The binding the occurrence belongs to.</param>
/// <remarks>
/// <para>
/// Written in the transaction that removes the row, and carrying nothing about the message, because once the erasure
/// cascade has run nothing else in the deployment knows where that message was on the source. It is how an erasure
/// always reaches the source without ever reaching it on the request that asked for one.
/// </para>
/// <para>
/// It is deleted when its expunge is observed complete, or when a UIDVALIDITY change means its UID names nothing any
/// more — so a remote path and a UID are kept for exactly as long as they name something to remove, and no longer. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </para>
/// </remarks>
public sealed record MailboxSourceRemoval(
    MailboxSourceRemovalId Id,
    EmailOccurrenceId Occurrence,
    MailFolderResolution Folder);

/// <summary>Identifies one source removal record.</summary>
/// <param name="Value">The generated identifier.</param>
public readonly record struct MailboxSourceRemovalId(Guid Value)
{
    /// <summary>Mints an identifier for a record being written.</summary>
    /// <returns>An identifier nothing else will produce, ordered by when it was minted.</returns>
    public static MailboxSourceRemovalId New() => new(Guid.CreateVersion7());
}
