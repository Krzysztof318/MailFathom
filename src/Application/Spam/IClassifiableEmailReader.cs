// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Spam;

/// <summary>Finds the mail classification may run over, and the account and folder of each occurrence it names.</summary>
/// <remarks>
/// <para>
/// A port of its own rather than a reuse of a mailbox read, because those apply the visibility a caller is entitled to
/// and classification is not a caller: it runs over stored mail on the operator's behalf, including over a folder no
/// tool may read. Reading it through a listing would silently leave such a folder unclassified.
/// </para>
/// <para>
/// Nothing here reaches a mail server. Both members read what an earlier synchronization run already committed, which is
/// what keeps classification — the single occurrence and the walk of a whole mailbox alike — unable to open an IMAP
/// session and therefore unable to touch a remote <c>\Seen</c> flag.
/// </para>
/// </remarks>
public interface IClassifiableEmailReader
{
    /// <summary>Finds one stored occurrence.</summary>
    /// <param name="emailId">The stable local identifier.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The occurrence, or <see langword="null" /> when nothing is stored under that identifier.</returns>
    /// <remarks>
    /// An absent occurrence is an ordinary answer: mail can be expunged between the moment classification was asked for
    /// and the moment it runs, and that is the message leaving rather than a failure to report.
    /// </remarks>
    Task<ClassifiableEmail?> FindAsync(StoredEmailId emailId, CancellationToken cancellationToken);

    /// <summary>Reads one account's stored occurrences in identity order, narrowed to a set of folders.</summary>
    /// <param name="accountId">The account whose mailbox is walked.</param>
    /// <param name="folderAliases">MailFathom's own names for the folders the walk covers.</param>
    /// <param name="resumeAfter">The identity the run last committed, or <see langword="null" /> to start at the beginning.</param>
    /// <param name="batchSize">How many occurrences to read at most.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The occurrences, which is empty once the walk has reached the end of the scope.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folderAliases" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A keyset read ordered by the stored email's identity, because that is the only ordering that is total, stable, and
    /// unaffected by anything a later write does to a row. The walk does not shrink as it progresses — a classified
    /// message is still a stored message — which is exactly why the run commits the position it reached rather than
    /// relying on the query to stop offering what it has already seen. An empty set of aliases names no mail and the
    /// answer is empty, which is what a scope narrowed to nothing means rather than a scope of everything.
    /// </remarks>
    Task<IReadOnlyList<ClassifiableEmail>> GetStoredEmailsAsync(
        MailAccountId accountId,
        IReadOnlyList<MailFolderAlias> folderAliases,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken);
}
