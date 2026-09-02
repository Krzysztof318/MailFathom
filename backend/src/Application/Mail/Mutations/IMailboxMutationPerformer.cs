// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Performs one authored change to a remote mailbox, through the durable record that makes it repeatable.</summary>
/// <remarks>
/// <para>
/// This is the only way anything asks for a mutation. Reaching <see cref="IMailboxWriteSession" /> directly would issue
/// a command nothing wrote down first, and the record is not bookkeeping beside the change: it is what makes the change
/// safe to ask for twice, what a retry resumes from, and what later tells a change MailFathom made apart from the same
/// change made by hand.
/// </para>
/// <para>
/// Asking again with the same request is the ordinary case rather than a mistake. It performs the change once, and
/// every call after that is answered from the record without a connection being opened.
/// </para>
/// </remarks>
public interface IMailboxMutationPerformer
{
    /// <summary>Writes the change down and carries it as far as it can get.</summary>
    /// <param name="request">The change being asked for.</param>
    /// <param name="folder">The alias binding the occurrence belongs to, whose remote folder is selected for writing.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy the write session must obey.</param>
    /// <param name="cancellationToken">Cancels the durable writes, opening the session, and the mail server commands.</param>
    /// <returns>What this call did, and where the record of it can be read.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" />, <paramref name="folder" />, or <paramref name="transportSecurityPolicy" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="folder" /> does not name the binding the request's occurrence belongs to.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the mutation within its configured resilience budget and attempts remain.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when the durable record could not be written because another writer changed it first.</exception>
    /// <remarks>
    /// A failure that leaves attempts remaining is raised, because the caller decides whether to ask again and when. A
    /// failure that spends the last attempt is recorded as terminal first and then raised, so the next call is answered
    /// from the record rather than opening a connection for a change nothing will make.
    /// </remarks>
    Task<MailboxMutationOutcome> PerformAsync(
        MailboxMutationRequest request,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken);
}
