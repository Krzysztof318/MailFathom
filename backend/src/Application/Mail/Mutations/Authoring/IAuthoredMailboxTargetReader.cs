// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
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
    /// <returns>The occurrence and its folder binding, or <see langword="null" /> when this deployment holds no occurrence to change under that identity.</returns>
    /// <remarks>
    /// <para>
    /// Two rows this deployment does hold answer as absent, and both exclusions belong here rather than to whichever
    /// adapter implements the read, because the caller turns either into the refusal a client is given. A tombstoned
    /// row answers as absent on the same terms every read of stored mail applies. So does a row whose remote occurrence
    /// the server has expunged, including a local copy retained after MailFathom deleted the message — a listing serves
    /// that one, because the mail is still readable, while the UID it carries names a message the server no longer
    /// holds, so a change recorded against it could only be attempted and fail.
    /// </para>
    /// </remarks>
    Task<AuthoredMailboxTarget?> FindAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken);
}

/// <summary>Where one stored email is, as a mutation has to name it.</summary>
/// <param name="Owner">The owner whose account the email belongs to, which every record the change writes carries.</param>
/// <param name="Occurrence">The account, folder binding, UIDVALIDITY, and UID an IMAP command is issued against.</param>
/// <param name="Folder">The binding the occurrence belongs to, including the remote path a write session selects.</param>
/// <remarks>
/// The two travel together for the reason <see cref="OutstandingMailboxMutation" /> gives: an occurrence names a folder
/// without saying where it is, and performing the change needs the remote path while reporting it needs the alias.
/// Nothing derived from the message is carried, so deciding whether a change may be made never reads mail.
/// </remarks>
public sealed record AuthoredMailboxTarget(
    MailOwnerId Owner,
    EmailOccurrenceId Occurrence,
    MailFolderResolution Folder);
