// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Synchronization.Restore;

/// <summary>One <c>APPEND</c> the restore issued whose answer never came back.</summary>
/// <param name="Id">What an operator settles the record by.</param>
/// <param name="Email">The message the append carried, which is what the restore will not carry again while this stands.</param>
/// <param name="SourceFolderAlias">MailFathom's own name for the folder the copy was appended into.</param>
/// <param name="SourceFolderGeneration">Which binding of that alias the command went out against.</param>
/// <param name="IssuedAt">When the command went out, which is how long the record has been standing.</param>
/// <remarks>
/// <para>
/// The record is written before the command and deleted once the server has named where the copy went, so a record
/// that exists at all is an append whose outcome is unknown: the folder may hold the copy and may not, and nothing
/// the folder shows afterwards tells a copy MailFathom appended apart from one a person put there. Reissuing it would
/// put a second message in somebody's mailbox, so the account is held in
/// <see cref="Domain.Accounts.MailAccountCustodyPhase.Restoring" /> until an operator says which of the two happened.
/// </para>
/// <para>
/// The generation is part of the folder the command went out against rather than a detail beside the alias. An alias
/// is repointed by a rewritten mapping or by discovery matching a different advertised folder, and a new generation is
/// what says so — two unrelated remote folders may advertise the same <c>UIDVALIDITY</c>, so an occurrence written
/// under the alias alone would name the new binding while carrying the old folder's identity. A record whose
/// generation no longer matches what the alias binds is therefore left standing for an operator rather than carried.
/// </para>
/// <para>
/// It names a message by its identity and a folder by its alias and generation, which are MailFathom's own words for
/// things. No subject, address, or content is here, for the reason the drain's own figures carry none. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </para>
/// </remarks>
public sealed record MailboxRestoreAppend(
    MailboxRestoreAppendId Id,
    StoredEmailId Email,
    MailFolderAlias SourceFolderAlias,
    MailFolderResolutionGeneration SourceFolderGeneration,
    DateTimeOffset IssuedAt);

/// <summary>Identifies one restore append record.</summary>
/// <param name="Value">The generated identifier.</param>
public readonly record struct MailboxRestoreAppendId(Guid Value)
{
    /// <summary>Mints an identifier for a record being written.</summary>
    /// <returns>An identifier nothing else will produce, ordered by when it was minted.</returns>
    public static MailboxRestoreAppendId New() => new(Guid.CreateVersion7());
}
