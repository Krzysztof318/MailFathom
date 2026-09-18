// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;

namespace MailFathom.Application.Contacts.Correspondence;

/// <summary>Reads what the stored mail and the attachment index already hold about a set of addresses.</summary>
/// <remarks>
/// <para>
/// Two reads rather than one, because they are two indexes: the conversations come off the stored messages and the
/// documents off the attachment rows derived from them. A single answer would make a mailbox whose derivation has not
/// caught up look like a mailbox with no correspondence in it.
/// </para>
/// <para>
/// Neither read decides what a caller may see. The scope is handed in, already resolved, and is the same narrowing
/// every other mail-returning read composes — an implementation that narrowed by anything of its own would be a second
/// reading of a caller's entitlement.
/// </para>
/// </remarks>
public interface IContactCorrespondenceIndex
{
    /// <summary>Reads the most recent conversations naming any of the addresses.</summary>
    /// <param name="scope">The accounts and folders the caller may read, which the query is narrowed by.</param>
    /// <param name="normalizedAddresses">The comparison forms of the contact's addresses, which is the only form anything here matches on.</param>
    /// <param name="correspondedOnOrAfter">The start of the window, before which nothing is read.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>At most <see cref="ContactCorrespondenceBounds.Threads" /> conversations, newest first, and empty where the window holds none.</returns>
    /// <remarks>
    /// A message whose received instant is unknown is outside every window and is therefore absent, which follows from
    /// the comparison rather than from a decision here and is the honest answer: nobody can say it arrived inside the
    /// window somebody asked about.
    /// </remarks>
    Task<IReadOnlyList<CorrespondingThread>> ReadRecentThreadsAsync(
        MailboxScope scope,
        IReadOnlyList<string> normalizedAddresses,
        DateTimeOffset correspondedOnOrAfter,
        CancellationToken cancellationToken);

    /// <summary>Reads the most recent documents sent from any of the addresses.</summary>
    /// <param name="scope">The accounts and folders the caller may read, which the query is narrowed by.</param>
    /// <param name="normalizedAddresses">The comparison forms of the contact's addresses, matched against the message's sender alone.</param>
    /// <param name="correspondedOnOrAfter">The start of the window, before which nothing is read.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>At most <see cref="ContactCorrespondenceBounds.Documents" /> documents, newest first, and empty where the window holds none.</returns>
    Task<IReadOnlyList<CorrespondingDocument>> ReadRecentDocumentsAsync(
        MailboxScope scope,
        IReadOnlyList<string> normalizedAddresses,
        DateTimeOffset correspondedOnOrAfter,
        CancellationToken cancellationToken);
}
