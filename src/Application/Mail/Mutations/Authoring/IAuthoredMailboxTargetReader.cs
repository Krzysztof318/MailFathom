// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Mutations.Authoring;

/// <summary>Answers where one stored email currently is, in the identities an IMAP command is issued against.</summary>
/// <remarks>
/// <para>
/// Every other requester of a mutation already holds this: a rule and a classification both act inside a run that met
/// the occurrence, so neither has to look it up. A caller naming an email by its local identifier holds none of it, and
/// this is the whole of what it needs — the occurrence the command is issued against, and the folder binding whose
/// remote path a write session selects.
/// </para>
/// <para>
/// It is read from the local mailbox copy and reaches no mail server, which is what keeps a protocol request from
/// resolving an alias over the network before it has been decided whether the caller may write at all. A row whose
/// folder binding has since been repointed answers under the binding the row carries, so the command is issued against
/// the occurrence the copy actually describes rather than against a folder the email may no longer be in.
/// </para>
/// </remarks>
public interface IAuthoredMailboxTargetReader
{
    /// <summary>Finds where one stored email is, or reports that this deployment holds no such row.</summary>
    /// <param name="storedEmailId">The email a caller named.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The occurrence and its folder binding, or <see langword="null" /> when no row carries that identity.</returns>
    Task<AuthoredMailboxTarget?> FindAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken);
}

/// <summary>Where one stored email is, as a mutation has to name it.</summary>
/// <param name="Occurrence">The account, folder binding, UIDVALIDITY, and UID an IMAP command is issued against.</param>
/// <param name="Folder">The binding the occurrence belongs to, including the remote path a write session selects.</param>
/// <remarks>
/// The two travel together for the reason <see cref="OutstandingMailboxMutation" /> gives: an occurrence names a folder
/// without saying where it is, and performing the change needs the remote path while reporting it needs the alias.
/// Nothing derived from the message is carried, so deciding whether a change may be made never reads mail.
/// </remarks>
public sealed record AuthoredMailboxTarget(EmailOccurrenceId Occurrence, MailFolderResolution Folder);
