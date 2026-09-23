// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts.Correspondence;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>Reads what the stored mail and the attachment index hold about a contact's addresses, out of PostgreSQL.</summary>
/// <remarks>
/// <para>
/// Both reads compose <see cref="StoredEmailSelectionPredicate.WithinScope" /> and nothing of their own, so what a
/// caller may see is decided here by the same expression every other mail-returning read is narrowed by — tombstones,
/// the accounts the caller is assigned, the folders a mapping admits, and the junk folder the scope withheld.
/// </para>
/// <para>
/// Neither read groups. The conversations are cut from a bounded walk of the most recent messages naming the contact,
/// ordered by the received instant the mail is already indexed by, so PostgreSQL serves an ordered window rather than
/// an aggregate over everything the year admits — and the walk's bound is what the distinct conversations are then
/// taken from in this process, which costs one pass over at most
/// <see cref="ContactCorrespondenceBounds.ScannedMessages" /> rows.
/// </para>
/// <para>
/// A conversation is matched against the sender and both recipient arrays, which is the same reading of <em>naming
/// this person</em> the mailbox filters already use; the recipient arrays carry GIN indexes, which is the access an
/// overlap against a set of addresses is planned for. A document is matched against the sender alone, because a file
/// somebody else attached to a conversation this contact was copied on is not a document this contact sent.
/// </para>
/// <para>
/// Every file name and subject this returns is mail content. Nothing here writes one to a log, a metric, a trace, or
/// an error message.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ContactCorrespondenceIndex(MailFathomDbContext dbContext) : IContactCorrespondenceIndex
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CorrespondingThread>> ReadRecentThreadsAsync(
        MailboxScope scope,
        IReadOnlyList<string> normalizedAddresses,
        DateTimeOffset correspondedOnOrAfter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(normalizedAddresses);

        if (normalizedAddresses.Count is 0)
        {
            return [];
        }

        var scanned = await this.ScannedThreadMessagesQuery(scope, normalizedAddresses, correspondedOnOrAfter)
            .ToArrayAsync(cancellationToken);

        return ThreadsOf(scanned);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CorrespondingDocument>> ReadRecentDocumentsAsync(
        MailboxScope scope,
        IReadOnlyList<string> normalizedAddresses,
        DateTimeOffset correspondedOnOrAfter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(normalizedAddresses);

        if (normalizedAddresses.Count is 0)
        {
            return [];
        }

        var rows = await this.RecentDocumentsQuery(scope, normalizedAddresses, correspondedOnOrAfter)
            .ToArrayAsync(cancellationToken);

        return DocumentsOf(rows);
    }

    /// <summary>Composes the bounded walk the conversations are cut from.</summary>
    /// <param name="scope">The accounts and folders the caller may read.</param>
    /// <param name="normalizedAddresses">The comparison forms of the contact's addresses.</param>
    /// <param name="correspondedOnOrAfter">The start of the window.</param>
    /// <returns>The composed query, which nothing has executed yet.</returns>
    /// <remarks>
    /// Exposed so the generated command can be read by a test: that the address set reaches both recipient arrays as an
    /// overlap rather than as a term per address, and that the walk's bound is PostgreSQL's rather than this process's,
    /// are claims about the command that a result would look identical either way.
    /// </remarks>
    internal IQueryable<CorrespondingThreadRow> ScannedThreadMessagesQuery(
        MailboxScope scope,
        IReadOnlyList<string> normalizedAddresses,
        DateTimeOffset correspondedOnOrAfter) =>
        ScannedThreadMessages(
            StoredEmailSelectionPredicate.WithinScope(dbContext.StoredEmails.AsNoTracking(), scope),
            normalizedAddresses,
            correspondedOnOrAfter);

    /// <summary>Selects the most recent messages exchanged with the addresses, newest first, which the threads are read from.</summary>
    /// <param name="readable">The mail the caller may read.</param>
    /// <param name="normalizedAddresses">The contact's addresses, normalized.</param>
    /// <param name="correspondedOnOrAfter">The oldest instant a message may be dated and still count.</param>
    /// <returns>The scanned messages, at most <see cref="ContactCorrespondenceBounds.ScannedMessages" /> of them.</returns>
    internal static IQueryable<CorrespondingThreadRow> ScannedThreadMessages(
        IQueryable<StoredEmailEntity> readable,
        IReadOnlyList<string> normalizedAddresses,
        DateTimeOffset correspondedOnOrAfter)
    {
        var addresses = normalizedAddresses.ToArray();

        return readable
            .Where(email => email.ReceivedAt >= correspondedOnOrAfter
                && email.EmailThreadId != null
                && ((email.SenderNormalizedAddress != null && addresses.Contains(email.SenderNormalizedAddress))
                    || email.ToAddresses.Any(address => addresses.Contains(address))
                    || email.CcAddresses.Any(address => addresses.Contains(address))))
            .OrderByDescending(email => email.ReceivedAt)
            .ThenByDescending(email => email.Id)
            .Select(email => new CorrespondingThreadRow(
                email.EmailThreadId!.Value,
                email.Id,
                email.Subject,
                email.ReceivedAt!.Value))
            .Take(ContactCorrespondenceBounds.ScannedMessages);
    }

    /// <summary>Reads the scanned messages as the threads they belong to, each named by its most recent message.</summary>
    /// <param name="scanned">The scanned messages, newest first.</param>
    /// <returns>The threads, most recent first, at most <see cref="ContactCorrespondenceBounds.Threads" /> of them.</returns>
    internal static IReadOnlyList<CorrespondingThread> ThreadsOf(IEnumerable<CorrespondingThreadRow> scanned) =>
    [
        .. scanned
            .DistinctBy(static row => row.EmailThreadId)
            .Take(ContactCorrespondenceBounds.Threads)
            .Select(static row => new CorrespondingThread(
                EmailThreadId.Create(row.EmailThreadId),
                StoredEmailId.Create(row.StoredEmailId),
                row.Subject,
                row.ReceivedAt)),
    ];

    /// <summary>Composes the query the documents are read from.</summary>
    /// <param name="scope">The accounts and folders the caller may read.</param>
    /// <param name="normalizedAddresses">The comparison forms of the contact's addresses.</param>
    /// <param name="correspondedOnOrAfter">The start of the window.</param>
    /// <returns>The composed query, which nothing has executed yet.</returns>
    /// <remarks>
    /// Exposed for the reason the walk above is, and for one of its own: that the sender alone decides which files are
    /// read is the whole of what keeps somebody else's attachment out of this contact's documents.
    /// </remarks>
    internal IQueryable<CorrespondingDocumentRow> RecentDocumentsQuery(
        MailboxScope scope,
        IReadOnlyList<string> normalizedAddresses,
        DateTimeOffset correspondedOnOrAfter) =>
        RecentDocuments(
            StoredEmailSelectionPredicate.WithinScope(dbContext.StoredEmails.AsNoTracking(), scope),
            normalizedAddresses,
            correspondedOnOrAfter);

    /// <summary>Selects the files the addresses sent most recently, newest first.</summary>
    /// <param name="readable">The mail the caller may read.</param>
    /// <param name="normalizedAddresses">The contact's addresses, normalized.</param>
    /// <param name="correspondedOnOrAfter">The oldest instant a message may be dated and still count.</param>
    /// <returns>The files, at most <see cref="ContactCorrespondenceBounds.Documents" /> of them.</returns>
    internal static IQueryable<CorrespondingDocumentRow> RecentDocuments(
        IQueryable<StoredEmailEntity> readable,
        IReadOnlyList<string> normalizedAddresses,
        DateTimeOffset correspondedOnOrAfter)
    {
        var addresses = normalizedAddresses.ToArray();

        var sent = readable
            .Where(email => email.ReceivedAt >= correspondedOnOrAfter
                && email.SenderNormalizedAddress != null
                && addresses.Contains(email.SenderNormalizedAddress));

        var documents = from email in sent
                        from attachment in email.AttachmentTexts
                        orderby email.ReceivedAt descending, email.Id descending, attachment.AttachmentPosition
                        select new CorrespondingDocumentRow(
                            email.Id,
                            attachment.AttachmentPosition,
                            attachment.FileName,
                            attachment.DeclaredMediaType,
                            email.ReceivedAt!.Value);

        return documents.Take(ContactCorrespondenceBounds.Documents);
    }

    /// <summary>Reads the selected files as the documents a card is derived from.</summary>
    /// <param name="rows">The files, newest first.</param>
    /// <returns>The documents, in the same order.</returns>
    internal static IReadOnlyList<CorrespondingDocument> DocumentsOf(IEnumerable<CorrespondingDocumentRow> rows) =>
    [
        .. rows.Select(static row => new CorrespondingDocument(
            StoredEmailId.Create(row.StoredEmailId),
            row.AttachmentPosition,
            row.FileName,
            row.DeclaredMediaType,
            row.ReceivedAt)),
    ];
}
