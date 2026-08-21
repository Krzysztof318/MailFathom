// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Spam.Actions;

/// <summary>Where a classified email is and how it stands, which is everything an action has to know about it.</summary>
/// <param name="Id">The local email, which is what a mutation record hangs off and what its filing history is read by.</param>
/// <param name="Occurrence">
/// Where the email is on the mail server, which is what an IMAP command is issued against. It is read now rather than
/// derived from the classification, because a classification is a durable record and the message may have moved since.
/// </param>
/// <param name="FolderAlias">
/// MailFathom's own name for the folder the occurrence is in, which is what decides whether a filing has anywhere left
/// to move the message to.
/// </param>
/// <param name="IsRemotelySeen">
/// Whether the mail server already reports the message read, as of the last synchronization. A message that is already
/// read is not written to, which keeps the flag change an act rather than a repeated statement.
/// </param>
/// <remarks>
/// Nothing here is mail content: a local identifier, a remote occurrence, a folder alias, and a flag are MailFathom's own
/// or the server's own names for things. That is what lets an action be decided and written down without the message it
/// is about being read again.
/// </remarks>
public sealed record SpamActionOccurrence(
    StoredEmailId Id,
    EmailOccurrenceId Occurrence,
    MailFolderAlias FolderAlias,
    bool IsRemotelySeen);
