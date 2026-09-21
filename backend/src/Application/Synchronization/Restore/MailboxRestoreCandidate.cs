// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Synchronization.Restore;

/// <summary>One stored message of a restoring account that its source server no longer holds.</summary>
/// <param name="Email">The stored message, by the identity that outlives an occurrence.</param>
/// <param name="SourceFolderAlias">The alias of the mapping whose folder the message is appended back into.</param>
/// <param name="State">The flags and keywords the copy carries onto the source.</param>
/// <param name="InternalDate">The instant the copy is appended under, which is the arrival the row recorded.</param>
/// <remarks>
/// <para>
/// The alias is carried rather than the resolution, because the folder the message goes back into is decided from the
/// account's configuration at the moment the append is issued: a restore appends into the source folder the message's
/// local folder maps onto, which is a mapping an operator may have rewritten since the drain took the message off.
/// </para>
/// <para>
/// Nothing of the message itself is here. The payload is read from the content store when the append is issued, so a
/// pass that takes a hundred candidates in hand holds a hundred identities rather than a hundred mailboxes' worth of
/// mail.
/// </para>
/// </remarks>
public sealed record MailboxRestoreCandidate(
    StoredEmailId Email,
    MailFolderAlias SourceFolderAlias,
    RestoredEmailState State,
    DateTimeOffset InternalDate);
