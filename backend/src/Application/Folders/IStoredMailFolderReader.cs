// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;

namespace MailFathom.Application.Folders;

/// <summary>Reads where each folder in a mailbox scope sits on its mail server, and how much of it is stored locally.</summary>
/// <remarks>
/// <para>
/// It exists beside <see cref="Synchronization.Checkpoints.ISynchronizationFreshnessReader" /> rather than replacing it,
/// because the two answer different questions and cost different amounts. Freshness is attached to every mailbox query
/// and is one instant per alias; this one counts mail, which is work proportional to the folder rather than to the
/// folder list, so it is asked for by the one read that draws a folder tree and by nothing that lists messages.
/// </para>
/// <para>
/// Implementations join no transaction, mutate nothing, and reach no mail server. The counts are of what is stored
/// locally at the moment of the read, which is what a caller may present beside the folder's own freshness and never as
/// the mail server's own figure.
/// </para>
/// </remarks>
public interface IStoredMailFolderReader
{
    /// <summary>Reads one entry per folder of the scope that local state holds a binding for.</summary>
    /// <param name="scope">The accounts and folders to report on.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>One entry per such folder, ordered ordinally by account and then by alias.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// A folder the scope names but nothing has ever bound to a remote folder appears in no entry, because there is no
    /// remote folder to report and no mail to count. A caller composes the folder list from what it already knows the
    /// scope holds and reads an absence here as exactly that.
    /// </remarks>
    Task<IReadOnlyList<StoredMailFolder>> ReadAsync(MailboxScope scope, CancellationToken cancellationToken);
}
