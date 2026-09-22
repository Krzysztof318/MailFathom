// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Synchronization.Restore;

/// <summary>An append the source answered in full, waiting for the occurrence to be written onto the message.</summary>
/// <param name="Record">The record the append was issued under.</param>
/// <param name="UidValidity">The UIDVALIDITY the source named for the folder it put the copy in.</param>
/// <param name="Uid">The UID the source named inside that folder.</param>
/// <remarks>
/// It exists because the answer and the occurrence cannot commit together: the answer arrives from a mail server and
/// the occurrence is written to PostgreSQL, so a pass shut down, rebalanced, or meeting a transient database failure
/// between the two would otherwise lose a placement the source has already stated. Recording the placement on its own
/// turns that window into work the next pass finishes rather than an append an operator has to establish by hand.
/// </remarks>
public sealed record MailboxRestoreConfirmation(
    MailboxRestoreAppend Record,
    ImapUidValidity UidValidity,
    ImapUid Uid);
